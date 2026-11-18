"""Convert a local Open Products Facts export into non-food staging CSV.

This module DOES NOT download data or grant a license for reuse.
Only records with explicit Indonesia country tags and clear non-food
category tags are emitted. Prices, stock and other private columns
are intentionally discarded.
"""
import argparse
import csv
import gzip
import json
import re
from pathlib import Path

from tools.stage_dataset import FIELDS as STAGING_FIELDS, valid_gtin

# Explicit category-tag matching only. No inference from product title.
TAG_MAP = (
    ("FASTENERS", ("en:fasteners", "en:bolts", "en:screws", "en:nails",
                   "en:washers", "en:hex-bolts")),
    ("SPARE_PARTS", ("en:auto-parts", "en:automotive-parts",
                     "en:vehicle-parts", "en:motorcycle-parts",
                     "en:spare-parts")),
    ("PLUMBING", ("en:plumbing", "en:pipes", "en:pipe-fittings",
                   "en:plumbing-fittings")),
    ("ELECTRICAL", ("en:electrical-supplies", "en:electric-cables",
                     "en:electrical-equipment")),
    ("TOOLS", ("en:tools", "en:hand-tools", "en:power-tools")),
    ("SHEET_MATERIALS", ("en:paper", "en:cardboard", "en:sheet-materials",
                          "en:wrapping-paper")),
    ("BUILDING_MATERIALS", ("en:building-materials", "en:coatings",
                             "en:adhesives", "en:construction-materials")),
    ("HOUSEHOLD_NONFOOD", ("en:household-supplies", "en:home-improvement",
                            "en:office-supplies", "en:stationery")),
)
FOOD_TAG_ROOTS = (
    "en:food", "en:foods", "en:beverages", "en:drinks", "en:snacks",
    "en:fruit", "en:vegetables", "en:meat", "en:dairy", "en:confectionery",
    "en:noodles", "en:coffee", "en:tea", "en:pet-food"
)
COUNTRY_TAGS = {"en:indonesia", "id:indonesia"}
REQUIRED = {"code", "product_name", "categories_tags", "countries_tags"}


def tokens(raw):
    return {x.strip().casefold() for x in (raw or "").split(",") if x.strip()}


def category_for(tags):
    if any(tag == root or tag.startswith(root + "-") for tag in tags for root in FOOD_TAG_ROOTS):
        return None
    for name, allowed in TAG_MAP:
        if tags.intersection(allowed):
            return name
    return None


def open_source(path):
    if path.suffix == ".gz":
        return gzip.open(path, "rt", encoding="utf-8-sig", newline="")
    return path.open("r", encoding="utf-8-sig", newline="")


def transform(source, destination, max_rows=300):
    source, destination = Path(source), Path(destination)
    if max_rows < 1 or max_rows > 5000:
        raise ValueError("max_rows must be 1..5000")
    if destination.exists():
        raise ValueError("Refusing to overwrite an existing file")
    if not destination.parent.exists():
        raise ValueError("Output directory does not exist")
    counters = {
        "scanned": 0, "missing_indonesia_tag": 0, "no_safe_category": 0,
        "invalid_gtin": 0, "missing_name": 0, "duplicate": 0, "emitted": 0
    }
    with open_source(source) as f, destination.open("w", encoding="utf-8", newline="") as out:
        sample = f.read(4096)
        f.seek(0)
        try:
            dialect = csv.Sniffer().sniff(sample, delimiters=",\t;")
        except csv.Error:
            dialect = csv.excel
        reader = csv.DictReader(f, dialect=dialect)
        headers = set(reader.fieldnames or [])
        missing = REQUIRED - headers
        if missing:
            raise ValueError("Missing input columns: " + ", ".join(sorted(missing)))
        writer = csv.DictWriter(out, fieldnames=STAGING_FIELDS)
        writer.writeheader()
        seen = set()
        for row in reader:
            counters["scanned"] += 1
            if not COUNTRY_TAGS.intersection(tokens(row.get("countries_tags", ""))):
                counters["missing_indonesia_tag"] += 1
                continue
            tags = tokens(row.get("categories_tags", ""))
            category = category_for(tags)
            if category is None:
                counters["no_safe_category"] += 1
                continue
            barcode = (row.get("code") or "").strip()
            if not valid_gtin(barcode):
                counters["invalid_gtin"] += 1
                continue
            name = (row.get("product_name") or "").strip()
            if not name:
                counters["missing_name"] += 1
                continue
            if barcode in seen:
                counters["duplicate"] += 1
                continue
            seen.add(barcode)
            attrs = {
                "source": "Open Products Facts",
                "country_tag_verified": True,
                "original_category_tags": sorted(tags),
                "unit_requires_store_confirmation": True,
            }
            writer.writerow({
                "external_id": barcode,
                "product_name": name,
                "brand": (row.get("brands") or "").strip(),
                "category_code": category,
                "variant": "",
                "unit_code": "PCS",  # provisional; store staff must confirm
                "barcode": barcode,
                "attributes_json": json.dumps(attrs, ensure_ascii=False, sort_keys=True),
                "product_url": "https://world.openproductsfacts.org/product/" + barcode,
            })
            counters["emitted"] += 1
            if counters["emitted"] >= max_rows:
                break
    return counters


def main():
    parser = argparse.ArgumentParser(description="Offline Open Products Facts -> nonfood staging CSV")
    parser.add_argument("--input", required=True, help="Local Open Products Facts CSV/TSV or .gz export")
    parser.add_argument("--out", required=True, help="Output staging CSV under data/private")
    parser.add_argument("--limit", type=int, default=300, help="Maximum emitted records, 1..5000")
    args = parser.parse_args()
    try:
        counters = transform(args.input, args.out, args.limit)
    except (OSError, ValueError, UnicodeError, csv.Error) as err:
        parser.exit(1, f"ERROR: {err}\n")
    print(json.dumps(counters, ensure_ascii=False, indent=2))
    print("ATTENTION: output is only a candidate set. License and store verification are still required.")


if __name__ == "__main__":
    main()
