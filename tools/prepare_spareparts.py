"""Produce a PII-free catalog-only staging CSV from a workshop part-master CSV.

The Mendeley workshop research source includes transaction and demographic fields.
This tool reads ONLY explicitly mapped part columns, never customer/service data.
It does not download source files, authorize dataset reuse, or set retail prices.
"""
import argparse
import csv
import json
from pathlib import Path

from tools.stage_dataset import FIELDS as STAGING_FIELDS


def transform(source, output, id_column, name_column, group_column=None,
              brand_column=None, limit=1000):
    source, output = Path(source), Path(output)
    if not 1 <= limit <= 5000:
        raise ValueError("limit must be 1..5000")
    if source.resolve() == output.resolve():
        raise ValueError("Input and output must differ")
    if output.exists():
        raise ValueError("Refusing to overwrite an existing output")
    if not output.parent.exists():
        raise ValueError("Output directory does not exist")
    if not id_column or not name_column or id_column == name_column:
        raise ValueError("Distinct part ID and description columns required")

    counters = {"scanned": 0, "emitted": 0, "missing_id_or_name": 0,
                "identical_duplicate": 0}
    with source.open("r", encoding="utf-8-sig", newline="") as input_file:
        sample = input_file.read(4096)
        input_file.seek(0)
        try:
            dialect = csv.Sniffer().sniff(sample, delimiters=",\t;")
        except csv.Error:
            dialect = csv.excel
        reader = csv.DictReader(input_file, dialect=dialect)
        required = {id_column, name_column}
        for optional in (group_column, brand_column):
            if optional:
                required.add(optional)
        if not required.issubset(reader.fieldnames or []):
            raise ValueError("Missing mapped source columns: "
                             + ", ".join(sorted(required - set(reader.fieldnames or []))))
        rows = []
        seen = {}
        for row in reader:
            counters["scanned"] += 1
            if None in row:
                raise ValueError("Invalid row with extra columns")
            part_id, name = (row.get(id_column) or "").strip(), (row.get(name_column) or "").strip()
            if not part_id or not name:
                counters["missing_id_or_name"] += 1
                continue
            if len(part_id) > 120:
                raise ValueError("part ID exceeds staging external_id limit")
            group = (row.get(group_column) or "").strip() if group_column else ""
            brand = (row.get(brand_column) or "").strip() if brand_column else ""
            descriptor = (name, group, brand)
            if part_id in seen:
                if seen[part_id] != descriptor:
                    raise ValueError(f"Conflicting descriptions for part ID {part_id}")
                counters["identical_duplicate"] += 1
                continue
            seen[part_id] = descriptor
            rows.append({
                "external_id": part_id,
                "product_name": name,
                "brand": brand,
                "category_code": "SPARE_PARTS",
                "variant": "",
                "unit_code": "PCS",
                "barcode": "",  # part identifier is NOT necessarily a GTIN
                "attributes_json": json.dumps({
                    "source": "Mendeley workshop part master (local CSV)",
                    "original_group": group,
                    "unit_requires_store_confirmation": True,
                    "barcode_not_verified": True,
                }, ensure_ascii=False, sort_keys=True),
                "product_url": "",
            })
            counters["emitted"] += 1
            if counters["emitted"] >= limit:
                break
    with output.open("w", encoding="utf-8", newline="") as file:
        writer = csv.DictWriter(file, fieldnames=STAGING_FIELDS)
        writer.writeheader()
        writer.writerows(rows)
    return counters


def main():
    parser = argparse.ArgumentParser(description="Extract a spare-part master; discard all other columns")
    parser.add_argument("--input", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--id-column", required=True, help="Source column for unique part ID")
    parser.add_argument("--name-column", required=True, help="Source column for spare-part description")
    parser.add_argument("--group-column", help="Optional source functional group")
    parser.add_argument("--brand-column", help="Optional brand column")
    parser.add_argument("--limit", type=int, default=1000)
    args = parser.parse_args()
    try:
        result = transform(args.input, args.out, args.id_column, args.name_column,
                           args.group_column, args.brand_column, args.limit)
    except (ValueError, OSError, csv.Error, UnicodeError) as exc:
        parser.exit(1, f"ERROR: {exc}\n")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    print("CANDIDATES ONLY. Source license/privacy review and store verification required.")


if __name__ == "__main__":
    main()
