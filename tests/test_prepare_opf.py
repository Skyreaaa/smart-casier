import csv
import gzip
import tempfile
import unittest
from pathlib import Path

from tools.prepare_opf import category_for, tokens, transform
from tools.stage_dataset import connect, init, stage_file


class OpenProductsFactsAdapterTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)
        self.export = self.dir / "synthetic_opf.csv"
        self.out = self.dir / "converted.csv"
        self.fields = (
            "code", "product_name", "brands", "categories_tags",
            "countries_tags", "customer_name", "sale_price_idr"
        )
        self.rows = [
            # Valid GTIN is not proof this is a real commercial product.
            ("4006381333931", "Baut Demo M8 x 30", "Test", "en:fasteners,en:screws",
             "en:indonesia", "PII MUST NOT EXPORT", "899000"),
            ("5012345678900", "Tape Demo", "Test", "en:adhesives",
             "en:indonesia", "PII MUST NOT EXPORT", "99000"),
            ("1234567890128", "Snack Demo", "Test", "en:foods,en:fasteners",
             "en:indonesia", "PII MUST NOT EXPORT", "1"),
            ("4006381333932", "Barcode Invalid", "Test", "en:screws",
             "en:indonesia", "PII MUST NOT EXPORT", "1"),
            ("4006381333931", "Duplicate Barcode", "Test", "en:screws",
             "en:indonesia", "PII MUST NOT EXPORT", "1"),
            ("4006381333931", "France only", "Test", "en:screws",
             "en:france", "PII MUST NOT EXPORT", "1"),
        ]
        self.write_rows(self.rows)

    def tearDown(self):
        self.tmp.cleanup()

    def write_rows(self, rows):
        with self.export.open("w", encoding="utf-8", newline="") as f:
            writer = csv.writer(f)
            writer.writerow(self.fields)
            writer.writerows(rows)

    def test_explicit_country_nonfood_and_pii_stripping(self):
        counters = transform(self.export, self.out, 20)
        self.assertEqual(counters["emitted"], 2)
        self.assertEqual(counters["missing_indonesia_tag"], 1)
        self.assertEqual(counters["no_safe_category"], 1)
        self.assertEqual(counters["invalid_gtin"], 1)
        self.assertEqual(counters["duplicate"], 1)
        with self.out.open(encoding="utf-8", newline="") as f:
            rows = list(csv.DictReader(f))
        self.assertEqual(len(rows), 2)
        self.assertNotIn("customer_name", rows[0])
        self.assertNotIn("sale_price_idr", rows[0])
        self.assertEqual(rows[0]["category_code"], "FASTENERS")
        self.assertEqual(rows[0]["unit_code"], "PCS")
        self.assertEqual(rows[0]["variant"], "")
        self.assertNotIn("PII MUST", self.out.read_text(encoding="utf-8"))

    def test_food_exclusion_takes_priority(self):
        self.assertIsNone(category_for(tokens("en:food-products,en:fasteners")))
        self.assertIsNone(category_for(tokens("en:snacks,en:tools")))
        self.assertEqual(category_for(tokens("en:fasteners")), "FASTENERS")

    def test_output_remains_held_by_staging_source(self):
        transform(self.export, self.out, 20)
        db = self.dir / "staging.db"
        init(db)
        with connect(db) as connection:
            stage_file(connection, "OPF_NONFOOD_INDONESIA", self.out)
            count = connection.execute(
                "SELECT COUNT(*) FROM staged_items WHERE status='HOLD_LICENSE'"
            ).fetchone()[0]
            self.assertEqual(count, 2)

    def test_output_guard_does_not_overwrite_file(self):
        self.out.write_text("protect", encoding="utf-8")
        with self.assertRaises(ValueError):
            transform(self.export, self.out)

    def test_missing_headers_rejected(self):
        self.export.write_text("code,product_name\n1,test\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            transform(self.export, self.out)

    def test_limit_is_bounded(self):
        with self.assertRaises(ValueError):
            transform(self.export, self.out, 0)
        with self.assertRaises(ValueError):
            transform(self.export, self.out, 5001)

    def test_gz_file_input(self):
        compressed = self.dir / "synthetic_opf.csv.gz"
        with self.export.open("rb") as original, gzip.open(compressed, "wb") as out:
            out.write(original.read())
        counters = transform(compressed, self.out, 20)
        self.assertEqual(counters["emitted"], 2)


if __name__ == "__main__":
    unittest.main()
