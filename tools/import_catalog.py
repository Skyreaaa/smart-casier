"""Validate and atomically import product catalog CSV. Python stdlib only.

Default = validation only; --apply requires an initialized SQLite database.
Prices are integers (IDR); quantities are integer milli-units internally.
"""
import argparse
import csv
import json
import re
import sqlite3
import uuid
from decimal import Decimal, InvalidOperation

FIELDS = ("product_key", "product_name", "category_code", "category_name",
          "product_type", "sku", "variant_label", "unit_code", "sale_price_idr",
          "track_stock", "opening_qty", "low_stock_threshold", "barcode",
          "aliases", "attributes_json")
KINDS = {"PHYSICAL", "DIGITAL", "SERVICE"}
UNITS = {"PCS", "M", "TXN"}
CODE = re.compile(r"^[A-Z0-9][A-Z0-9._-]{1,63}$")


def quantity(value, name):
    try:
        number = Decimal(str(value))
    except (InvalidOperation, ValueError):
        raise ValueError(f"{name}: invalid number") from None
    if not number.is_finite() or number < 0 or (number * 1000) != (number * 1000).to_integral_value():
        raise ValueError(f"{name}: must be nonnegative with <=3 decimals")
    return int(number * 1000)


def validate(path):
    with open(path, encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        if tuple(reader.fieldnames or ()) != FIELDS:
            raise ValueError("CSV header differs from template")
        source = list(reader)
    if not source:
        raise ValueError("No products in CSV")
    rows, seen_sku, seen_barcode, family, cats = [], set(), set(), {}, {}
    for line, raw in enumerate(source, 2):
        if None in raw:
            raise ValueError(f"Line {line}: extra CSV columns")
        r = {k: (raw[k] or "").strip() for k in FIELDS}
        for k in ("product_key", "product_name", "category_code", "category_name", "sku"):
            if not r[k]:
                raise ValueError(f"Line {line}: missing {k}")
        for k in ("product_key", "category_code", "sku"):
            r[k] = r[k].upper()
            if not CODE.fullmatch(r[k]):
                raise ValueError(f"Line {line}: invalid {k}")
        r["product_type"], r["unit_code"] = r["product_type"].upper(), r["unit_code"].upper()
        if r["product_type"] not in KINDS or r["unit_code"] not in UNITS:
            raise ValueError(f"Line {line}: invalid product_type or unit_code")
        if r["track_stock"] not in ("0", "1"):
            raise ValueError(f"Line {line}: track_stock must be 0 or 1")
        r["track_stock"] = int(r["track_stock"])
        if r["track_stock"] and (r["product_type"] != "PHYSICAL" or r["unit_code"] == "TXN"):
            raise ValueError(f"Line {line}: inventory can only track physical goods")
        r["opening_milli"] = quantity(r["opening_qty"], "opening_qty")
        r["threshold_milli"] = quantity(r["low_stock_threshold"], "low_stock_threshold")
        if r["unit_code"] != "M" and (r["opening_milli"] % 1000 or r["threshold_milli"] % 1000):
            raise ValueError(f"Line {line}: fractional pieces not allowed")
        if not r["track_stock"] and (r["opening_milli"] or r["threshold_milli"]):
            raise ValueError(f"Line {line}: untracked stock must be zero")
        try:
            r["sale_price_idr"] = int(r["sale_price_idr"])
        except ValueError:
            raise ValueError(f"Line {line}: invalid IDR price") from None
        if r["sale_price_idr"] < 0:
            raise ValueError(f"Line {line}: negative price")
        try:
            attrs = json.loads(r["attributes_json"] or "{}")
        except json.JSONDecodeError:
            raise ValueError(f"Line {line}: invalid attributes JSON") from None
        if not isinstance(attrs, dict):
            raise ValueError(f"Line {line}: attributes must be JSON object")
        r["attributes_json"] = json.dumps(attrs, ensure_ascii=False, sort_keys=True)
        r["alias_list"] = list(dict.fromkeys(a.strip() for a in r["aliases"].split("|") if a.strip()))
        descriptor = (r["product_name"], r["category_code"], r["product_type"])
        if r["product_key"] in family and family[r["product_key"]] != descriptor:
            raise ValueError(f"Line {line}: family mismatch")
        family[r["product_key"]] = descriptor
        if r["category_code"] in cats and cats[r["category_code"]] != r["category_name"]:
            raise ValueError(f"Line {line}: category mismatch")
        cats[r["category_code"]] = r["category_name"]
        if r["sku"] in seen_sku or (r["barcode"] and r["barcode"] in seen_barcode):
            raise ValueError(f"Line {line}: duplicate SKU or barcode")
        seen_sku.add(r["sku"])
        if r["barcode"]:
            seen_barcode.add(r["barcode"])
        rows.append(r)
    return rows


def import_rows(conn, rows):
    conn.execute("PRAGMA foreign_keys=ON")
    if conn.execute("PRAGMA user_version").fetchone()[0] < 1:
        raise ValueError("Database needs migration 001_core.sql")
    with conn:
        for r in rows:
            found = conn.execute("SELECT id,name FROM categories WHERE code=?", (r["category_code"],)).fetchone()
            if found:
                if found[1] != r["category_name"]:
                    raise ValueError("Category name conflict")
                cat_id = found[0]
            else:
                cat_id = uuid.uuid4().hex
                conn.execute("INSERT INTO categories(id,code,name) VALUES(?,?,?)",
                             (cat_id, r["category_code"], r["category_name"]))
            found = conn.execute(
                "SELECT id,name,category_id,product_type FROM products WHERE product_key=?",
                (r["product_key"],)).fetchone()
            if found:
                if found[1:] != (r["product_name"], cat_id, r["product_type"]):
                    raise ValueError("Product family conflict")
                product_id = found[0]
            else:
                product_id = uuid.uuid4().hex
                conn.execute(
                    "INSERT INTO products(id,product_key,category_id,name,product_type) VALUES(?,?,?,?,?)",
                    (product_id,r["product_key"],cat_id,r["product_name"],r["product_type"]))
            variant_id = uuid.uuid4().hex
            conn.execute(
                """INSERT INTO variants(id,product_id,sku,label,unit_code,sale_price_idr,
                   track_stock,low_stock_threshold_milli,attributes_json)
                   VALUES(?,?,?,?,?,?,?,?,?)""",
                (variant_id,product_id,r["sku"],r["variant_label"],r["unit_code"],
                 r["sale_price_idr"],r["track_stock"],r["threshold_milli"],r["attributes_json"]))
            if r["barcode"]:
                conn.execute("INSERT INTO barcodes(code,variant_id) VALUES(?,?)",
                             (r["barcode"],variant_id))
            for alias in r["alias_list"]:
                conn.execute("INSERT INTO variant_aliases(id,variant_id,alias) VALUES(?,?,?)",
                             (uuid.uuid4().hex,variant_id,alias))
            if r["opening_milli"]:
                conn.execute(
                    "INSERT INTO stock_movements(id,variant_id,movement_type,delta_qty_milli,note) "
                    "VALUES(?,?,'OPENING',?,'Initial CSV opening stock')",
                    (uuid.uuid4().hex,variant_id,r["opening_milli"]))
    return len(rows)


def main():
    p = argparse.ArgumentParser(description="Smart Casier CSV importer")
    p.add_argument("csv")
    p.add_argument("--apply", action="store_true")
    p.add_argument("--db", help="Existing SQLite database path")
    args = p.parse_args()
    try:
        rows = validate(args.csv)
        print(f"VALID: {len(rows)} SKUs, {len(set(r['product_key'] for r in rows))} families")
        if args.apply:
            if not args.db:
                p.error("--apply requires --db")
            with sqlite3.connect(args.db) as conn:
                import_rows(conn, rows)
            print("IMPORTED. Validate stock status with v_inventory_status.")
        else:
            print("DRY RUN: no database changes")
    except (ValueError, sqlite3.Error) as error:
        p.exit(1, f"ERROR: {error}\n")


if __name__ == "__main__":
    main()
