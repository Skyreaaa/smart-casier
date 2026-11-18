import csv
import tempfile
import unittest
from pathlib import Path

from tools.prepare_prdect import convert
from tools.stage_dataset import connect, init, stage_file


class PRDECTAdapterTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.source = self.root / "sample.csv"
        self.out = self.root / "candidates.csv"

    def tearDown(self):
        self.tmp.cleanup()

    def write_rows(self, rows):
        with self.source.open("w", encoding="utf-8", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=[
                "Category", "Product Name", "Location", "Price", "Customer Review"
            ])
            writer.writeheader()
            writer.writerows(rows)

    def rows(self):
        return [
            {"Category": "Automotive", "Product Name": "Baut M8 x 30",
             "Location": "Bogor", "Price": "3000", "Customer Review": "Personal text"},
            {"Category": "Automotive", "Product Name": "  Baut   M8 x 30 ",
             "Location": "Bandung", "Price": "9999", "Customer Review": "Private data"},
            {"Category": "Carpentry", "Product Name": "Mata bor set 10mm",
             "Location": "Jakarta", "Price": "45000", "Customer Review": "Private review"},
            {"Category": "Office & Stationery", "Product Name": "Spidol permanen",
             "Location": "Bali", "Price": "10000", "Customer Review": "Some other review"},
            {"Category": "Food and Drink", "Product Name": "Susu UHT",
             "Location": "Yogyakarta", "Price": "1000", "Customer Review": "Personal"},
            {"Category": "Automotive", "Product Name": "Snack keripik",
             "Location": "Yogyakarta", "Price": "1000", "Customer Review": "Personal"},
            {"Category": "Carpentry", "Product Name": "WA 081234567891 to order",
             "Location": "Jakarta", "Price": "15000", "Customer Review": "Customer"},
        ]

    def test_distinct_nonfood_names_and_discard_sensitive_fields(self):
        self.write_rows(self.rows())
        stats = convert(self.source, self.out)
        self.assertEqual(stats["emitted"], 3)
        self.assertEqual(stats["duplicate_name"], 1)
        self.assertEqual(stats["category_excluded"], 1)
        self.assertEqual(stats["food_or_contact_review"], 2)
        with self.out.open(encoding="utf-8", newline="") as f:
            records = list(csv.DictReader(f))
        self.assertEqual(len(records), 3)
        self.assertEqual(len({r["external_id"] for r in records}), 3)
        self.assertEqual(records[0]["category_code"], "AUTOMOTIVE_GENERAL")
        self.assertTrue(records[0]["external_id"].startswith("prdect-"))
        self.assertEqual(records[0]["unit_code"], "PCS")
        self.assertEqual(records[0]["barcode"], "")
        self.assertEqual(records[0]["brand"], "")
        for field in ("Location", "Price", "Customer Review"):
            self.assertNotIn(field, records[0])
        text = self.out.read_text(encoding="utf-8")
        self.assertNotIn("Personal text", text)
        self.assertNotIn("Private review", text)
        self.assertNotIn("45000", text)

    def test_lifecycle_to_license_hold(self):
        self.write_rows(self.rows())
        convert(self.source, self.out)
        database = self.root / "staging.db"
        init(database)
        with connect(database) as conn:
            stage_file(conn, "ID_PRDECT_HF_NONFOOD", self.out)
            self.assertEqual(conn.execute(
                "SELECT COUNT(*) FROM staged_items WHERE status='HOLD_LICENSE'"
            ).fetchone()[0], 3)

    def test_export_only_safely_mapped_categories(self):
        self.write_rows(self.rows())
        convert(self.source, self.out)
        with self.out.open(encoding="utf-8", newline="") as f:
            categories = {r["category_code"] for r in csv.DictReader(f)}
        self.assertEqual(categories, {
            "AUTOMOTIVE_GENERAL", "HARDWARE_GENERAL", "STATIONERY"
        })

    def test_id_stable_on_reordered_source(self):
        records = self.rows()
        self.write_rows(records)
        convert(self.source, self.out)
        with self.out.open(encoding="utf-8", newline="") as f:
            before = {(r["product_name"], r["external_id"]) for r in csv.DictReader(f)}
        self.out.unlink()
        self.write_rows(list(reversed(records)))
        convert(self.source, self.out)
        with self.out.open(encoding="utf-8", newline="") as f:
            after = {(r["product_name"], r["external_id"]) for r in csv.DictReader(f)}
        self.assertEqual(before, after)

    def test_guard_input_and_output(self):
        self.write_rows(self.rows())
        with self.assertRaises(ValueError):
            convert(self.source, self.out, limit=0)
        convert(self.source, self.out)
        with self.assertRaises(ValueError):
            convert(self.source, self.out)
        self.out.unlink()
        self.source.write_text("bad,header\nfoo,bar\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            convert(self.source, self.out)


if __name__ == "__main__":
    unittest.main()
