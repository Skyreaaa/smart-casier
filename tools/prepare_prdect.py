"""Convert PRDECT-ID Indonesian ecommerce product metadata into NONFOOD staging.

Reads a locally obtained CSV. Does not download or republish source data.
Discards reviews, customer location, prices, ratings and transactions.
Source and every record remain subject to provenance / license review.
"""
import argparse
import csv
import hashlib
import json
import re
import unicodedata
from pathlib import Path

from tools.stage_dataset import FIELDS as STAGING_FIELDS

CATEGORY_MAP = {
    "Automotive": "AUTOMOTIVE_GENERAL",
    "Carpentry": "HARDWARE_GENERAL",
    "Office & Stationery": "STATIONERY",
    "Electronics": "ELECTRONICS_GENERAL",
    "Household": "HOUSEHOLD_NONFOOD",
}
REQUIRED = {"Category", "Product Name"}
FOOD_TERMS = re.compile(
    r"\b(?:makanan|minuman|snack|keripik|biskuit|cokelat|coklat|"
    r"mie instan|mi instan|gula pasir|susu bubuk|beras|teh celup)\b",
    re.IGNORECASE,
)
CONTACT_TOKENS = re.compile(
    r"(?:https?://|www\.|[a-z0-9._%+\-]+@[a-z0-9.\-]+\.[a-z]{2,}|"
    r"\b(?:wa|whatsapp|hubungi|chat admin)\s*[:+0-9]|"
    r"(?<!\d)(?:\+?62|0)8\d{8,12}(?!\d))",
    re.IGNORECASE,
)


def canonical_name(value):
    return " ".join(unicodedata.normalize("NFKC", value or "").split())


def convert(source, out, limit=5000):
    source, out = Path(source), Path(out)
    if source.resolve() == out.resolve():
        raise ValueError("Input and output must differ")
    if out.exists():
        raise ValueError("Will not overwrite existing output")
    if not out.parent.is_dir():
        raise ValueError("Output directory must exist")
    if not 1 <= limit <= 10000:
        raise ValueError("limit must be between 1 and 10000")
    stats = {
        "scanned": 0, "category_excluded": 0, "empty_name": 0,
        "food_or_contact_review": 0, "duplicate_name": 0, "emitted": 0
    }
    seen = set()
    records = []
    with source.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        columns = set(reader.fieldnames or [])
        if not REQUIRED.issubset(columns):
            raise ValueError("Required columns: " + ", ".join(sorted(REQUIRED)))
        for row in reader:
            stats["scanned"] += 1
            if None in row:
                raise ValueError("Malformed source CSV row")
            original_category = (row.get("Category") or "").strip()
            category = CATEGORY_MAP.get(original_category)
            if category is None:
                stats["category_excluded"] += 1
                continue
            name = canonical_name(row.get("Product Name"))
            if not name:
                stats["empty_name"] += 1
                continue
            # This is deliberately conservative. Rejected rows are not exported.
            if FOOD_TERMS.search(name) or CONTACT_TOKENS.search(name):
                stats["food_or_contact_review"] += 1
                continue
            if len(name) > 400:
                stats["food_or_contact_review"] += 1
                continue
            dedupe_key = (original_category.casefold(), name.casefold())
            if dedupe_key in seen:
                stats["duplicate_name"] += 1
                continue
            seen.add(dedupe_key)
            digest = hashlib.sha256(
                (category + "\x1f" + name.casefold()).encode("utf-8")
            ).hexdigest()[:24]
            records.append({
                "external_id": "prdect-" + digest,
                "product_name": name,
                "brand": "",  # no trustworthy brand field in source
                "category_code": category,
                "variant": "",  # title may already include size/color; manual review required
                "unit_code": "PCS",  # provisional, not a confirmed stock unit
                "barcode": "",  # source has no barcode
                "attributes_json": json.dumps({
                    "source_category": original_category,
                    "category_requires_review": True,
                    "variant_requires_review": True,
                    "unit_requires_store_confirmation": True
                }, sort_keys=True, ensure_ascii=False),
                "product_url": ""
            })
            stats["emitted"] += 1
            if stats["emitted"] >= limit:
                break
    with out.open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=STAGING_FIELDS)
        writer.writeheader()
        writer.writerows(records)
    return stats


def main():
    parser = argparse.ArgumentParser(
        description="PRDECT-ID metadata-only nonfood candidate converter")
    parser.add_argument("--input", required=True, help="Locally obtained PRDECT-ID CSV")
    parser.add_argument("--out", required=True, help="Staging CSV, under data/private")
    parser.add_argument("--limit", type=int, default=5000)
    args = parser.parse_args()
    try:
        stats = convert(args.input, args.out, args.limit)
    except (OSError, UnicodeError, ValueError, csv.Error) as error:
        parser.exit(1, f"ERROR: {error}\n")
    print(json.dumps(stats, ensure_ascii=False, indent=2))
    print("METADATA ONLY. Source rights pending; not retail SKUs, stock, barcode or prices.")


if __name__ == "__main__":
    main()
