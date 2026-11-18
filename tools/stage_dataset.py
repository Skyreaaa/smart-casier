"""Offline staging of non-food external product records. Never writes to POS inventory."""
import argparse
import csv
import hashlib
import json
import re
import sqlite3
import sys
import unicodedata
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = ROOT / "database/migrations/002_dataset_staging.sql"
REGISTRY = ROOT / "data/sources/nonfood_registry.json"
FIELDS = ("external_id", "product_name", "brand", "category_code", "variant",
          "unit_code", "barcode", "attributes_json", "product_url")
ALLOWED_CATEGORIES = {
    "SPARE_PARTS", "FASTENERS", "PLUMBING", "ELECTRICAL", "TOOLS",
    "SHEET_MATERIALS", "BUILDING_MATERIALS", "HOUSEHOLD_NONFOOD",
    "DIGITAL_SERVICES",
}
UNITS = {"PCS", "M", "TXN"}
# Conservative flag for likely food falsely mapped to an allowed non-food category.
FOOD_WORDS = re.compile(r"\b(?:keripik|mi(?:e)? instan|snack|biskuit|susu|makanan|minuman|kopi bubuk|teh botol|beras|gula pasir)\b", re.IGNORECASE)


def utcnow():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def normalize(value):
    text = unicodedata.normalize("NFKC", value or "").casefold()
    return " ".join(re.findall(r"\w+", text, flags=re.UNICODE))


def valid_gtin(code):
    """Check EAN-8, UPC-A, EAN-13 and GTIN-14 check digits, preserving zeros."""
    if not code or not code.isascii() or not code.isdigit() or len(code) not in (8, 12, 13, 14):
        return False
    weighted = sum(int(ch) * (3 if i % 2 == 0 else 1)
                   for i, ch in enumerate(reversed(code[:-1])))
    return (10 - weighted % 10) % 10 == int(code[-1])


def registry():
    data = json.loads(REGISTRY.read_text(encoding="utf-8"))
    items = data["sources"]
    result = {s["id"]: s for s in items}
    if len(result) != len(items):
        raise ValueError("Duplicate source id in registry")
    return result


def connect(db):
    if not Path(db).exists():
        raise ValueError("Run init first; staging DB does not exist")
    connection = sqlite3.connect(db)
    connection.execute("PRAGMA foreign_keys=ON")
    if connection.execute("PRAGMA user_version").fetchone()[0] != 2:
        connection.close()
        raise ValueError("Invalid staging database version; expected migration 002")
    return connection


def init(db):
    db = Path(db)
    db.parent.mkdir(parents=True, exist_ok=True)
    if db.exists():
        raise ValueError("Database exists; refusing to overwrite existing staging data")
    conn = sqlite3.connect(db)
    try:
        conn.executescript(SCHEMA.read_text(encoding="utf-8"))
        with conn:
            for source in registry().values():
                conn.execute(
                    "INSERT INTO dataset_sources(id,title,url,license_name,review_status,scope,note)"
                    " VALUES(?,?,?,?,?,?,?)",
                    (source["id"], source["title"], source["url"], source["license_name"],
                     source["review_status"], source["scope"], source["note"]))
    except Exception:
        conn.close()
        db.unlink(missing_ok=True)
        raise
    conn.close()
    print(f"STAGING_READY: {db}")


def load_csv(path):
    with open(path, encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if tuple(reader.fieldnames or ()) != FIELDS:
            raise ValueError("CSV headers must exactly match nonfood staging template")
        for line, raw in enumerate(reader, start=2):
            if None in raw:
                raise ValueError(f"line {line}: extra CSV columns")
            record = {field: (raw[field] or "").strip() for field in FIELDS}
            if not record["external_id"] or not record["product_name"]:
                raise ValueError(f"line {line}: external_id and product_name required")
            if len(record["external_id"]) > 120:
                raise ValueError(f"line {line}: external_id too long")
            record["category_code"] = record["category_code"].upper()
            record["unit_code"] = record["unit_code"].upper()
            try:
                attrs = json.loads(record["attributes_json"] or "{}")
            except json.JSONDecodeError as exc:
                raise ValueError(f"line {line}: attributes_json malformed") from exc
            if not isinstance(attrs, dict):
                raise ValueError(f"line {line}: attributes_json must be JSON object")
            record["attributes_json"] = json.dumps(attrs, ensure_ascii=False, sort_keys=True)
            yield record


def fingerprint(row):
    fields = (row["product_name"], row["brand"], row["variant"], row["unit_code"],
              row["attributes_json"])
    combined = "\x1f".join(normalize(x) for x in fields)
    return hashlib.sha256(combined.encode("utf-8")).hexdigest()


def stage_file(conn, source_id, path):
    path = Path(path)
    source = conn.execute(
        "SELECT review_status FROM dataset_sources WHERE id=?", (source_id,)
    ).fetchone()
    if not source:
        raise ValueError("Source not in registry; add source with license review first")
    # A batch is idempotent; changed external IDs are never silently overwritten.
    rows = list(load_csv(path))
    if len(rows) != len({r["external_id"] for r in rows}):
        raise ValueError("Duplicate external_id within this file")
    counts = {}
    with conn:
        for row in rows:
            payload = json.dumps(row, sort_keys=True, ensure_ascii=False)
            digest = hashlib.sha256(payload.encode("utf-8")).hexdigest()
            existing = conn.execute(
                "SELECT payload_hash FROM staged_items WHERE source_id=? AND external_id=?",
                (source_id, row["external_id"])
            ).fetchone()
            if existing:
                if existing[0] != digest:
                    raise ValueError(f"Source record {row['external_id']} changed: manual review required")
                counts["UNCHANGED"] = counts.get("UNCHANGED", 0) + 1
                continue
            status, reason = "PENDING_REVIEW", ""
            if row["category_code"] not in ALLOWED_CATEGORIES:
                status, reason = "OUT_OF_SCOPE", "Category not in non-food allowlist"
            elif FOOD_WORDS.search(row["product_name"]):
                status, reason = "FOOD_REVIEW", "Food-related keywords in product name; manual review required"
            elif row["unit_code"] not in UNITS:
                status, reason = "INVALID_UNIT", "Unknown unit"
            elif row["category_code"] == "DIGITAL_SERVICES" and row["unit_code"] != "TXN":
                status, reason = "INVALID_UNIT", "Digital service must use TXN unit"
            elif row["barcode"] and not valid_gtin(row["barcode"]):
                status, reason = "INVALID_BARCODE", "GTIN check digit or length invalid"
            elif source[0] != "APPROVED":
                status, reason = "HOLD_LICENSE", "Dataset reuse rights not approved"
            else:
                fp = fingerprint(row)
                duplicate = conn.execute(
                    "SELECT id FROM staged_items WHERE (barcode<>'' AND barcode=?) OR fingerprint=?"
                    " LIMIT 1", (row["barcode"], fp)
                ).fetchone()
                if duplicate:
                    status, reason = "POSSIBLE_DUPLICATE", f"Potential duplicate of row {duplicate[0]}"
            fp = fingerprint(row)
            conn.execute(
                """INSERT INTO staged_items(
                     source_id,external_id,product_name,brand,category_code,variant,
                     unit_code,barcode,attributes_json,product_url,fingerprint,
                     payload_hash,status,reason,created_at)
                   VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)""",
                (source_id, row["external_id"], row["product_name"], row["brand"],
                 row["category_code"], row["variant"], row["unit_code"], row["barcode"],
                 row["attributes_json"], row["product_url"], fp, digest,
                 status, reason, utcnow())
            )
            counts[status] = counts.get(status, 0) + 1
    print(json.dumps({"source": source_id, "rows": len(rows), "status": counts},
                     indent=2, ensure_ascii=False))


def review(conn, item_id, decision, reviewer, note):
    reviewer = reviewer.strip()
    if not reviewer or not note.strip():
        raise ValueError("Reviewer identity and review note are required")
    with conn:
        row = conn.execute(
            "SELECT i.status,s.review_status FROM staged_items i "
            "JOIN dataset_sources s ON s.id=i.source_id WHERE i.id=?", (item_id,)
        ).fetchone()
        if not row:
            raise ValueError("Staging record not found")
        if decision == "APPROVE" and (row[0] != "PENDING_REVIEW" or row[1] != "APPROVED"):
            raise ValueError("Cannot approve held, invalid or duplicate records")
        if row[0] in ("APPROVED", "REJECTED"):
            raise ValueError("Record was already reviewed")
        state = "APPROVED" if decision == "APPROVE" else "REJECTED"
        conn.execute(
            "UPDATE staged_items SET status=?, reviewer=?, review_note=?,reviewed_at=? WHERE id=?",
            (state, reviewer, note.strip(), utcnow(), item_id)
        )
    print(f"REVIEWED: {item_id} -> {state}")


EXPORT_FIELDS = ("source_id", "source_external_id", "product_name", "brand",
                 "category_code", "variant", "unit_code", "barcode",
                 "attributes_json", "product_url", "license_name",
                 "sku", "sale_price_idr", "opening_qty", "low_stock_threshold")


def export_candidates(conn, outpath):
    """Export only reviewed metadata. Empty price/stock/SKU require store verification."""
    outpath = Path(outpath)
    if not outpath.parent.exists():
        raise ValueError("Output directory does not exist")
    if outpath.exists():
        raise ValueError("Refusing to overwrite exported file")
    rows = conn.execute(
        """SELECT i.source_id,i.external_id,i.product_name,i.brand,
                  i.category_code,i.variant,i.unit_code,i.barcode,
                  i.attributes_json,i.product_url,s.license_name
           FROM staged_items i JOIN dataset_sources s ON s.id=i.source_id
           WHERE i.status='APPROVED' AND s.review_status='APPROVED'
           ORDER BY i.source_id,i.external_id"""
    ).fetchall()
    with outpath.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=EXPORT_FIELDS)
        writer.writeheader()
        for row in rows:
            record = dict(zip(EXPORT_FIELDS, list(row) + ["", "", "", ""]))
            writer.writerow(record)
    print(f"EXPORTED: {len(rows)} approved metadata candidates to {outpath}")
    print("NOT READY FOR POS: SKU, sale price and opening stock must be verified at the store")


def report(conn):
    result = conn.execute(
        "SELECT status,COUNT(*) FROM staged_items GROUP BY status ORDER BY status"
    ).fetchall()
    print(json.dumps({"total": sum(v for _, v in result), "statuses": dict(result)},
                     indent=2, ensure_ascii=False))


def main():
    parser = argparse.ArgumentParser(description="Offline non-food catalog staging")
    parser.add_argument("--db", required=True, help="Private staging SQLite path")
    subs = parser.add_subparsers(dest="command", required=True)
    subs.add_parser("init")
    command = subs.add_parser("ingest")
    command.add_argument("--source", required=True)
    command.add_argument("--csv", required=True)
    command = subs.add_parser("review")
    command.add_argument("--id", type=int, required=True)
    command.add_argument("--decision", choices=("APPROVE", "REJECT"), required=True)
    command.add_argument("--reviewer", required=True)
    command.add_argument("--note", required=True)
    command = subs.add_parser("export")
    command.add_argument("--out", required=True)
    subs.add_parser("report")
    args = parser.parse_args()
    try:
        if args.command == "init":
            init(args.db)
            return
        with connect(args.db) as conn:
            if args.command == "ingest":
                stage_file(conn, args.source, args.csv)
            elif args.command == "review":
                review(conn, args.id, args.decision, args.reviewer, args.note)
            elif args.command == "export":
                export_candidates(conn, args.out)
            elif args.command == "report":
                report(conn)
    except (ValueError, sqlite3.Error, OSError) as exc:
        parser.exit(1, f"ERROR: {exc}\n")


if __name__ == "__main__":
    main()
