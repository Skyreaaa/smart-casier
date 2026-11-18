"""Regression tests for non-food staging and license/quality gates."""
import csv
import json
import sqlite3
import tempfile
import unittest
from pathlib import Path

from tools.stage_dataset import (
    FIELDS, connect, export_candidates, init, review, stage_file,
    valid_gtin, review_source,
)

ROOT = Path(__file__).resolve().parents[1]
DEMO = ROOT / "data/examples/nonfood_staging_demo.csv"


class StagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.db = self.root / "staging.db"
        init(self.db)
        self.conn = connect(self.db)

    def tearDown(self):
        self.conn.close()
        self.temp.cleanup()

    def rows(self):
        with DEMO.open(encoding="utf-8", newline="") as f:
            return list(csv.DictReader(f))

    def make_csv(self, rows):
        path = self.root / "sample.csv"
        with path.open("w", encoding="utf-8", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=FIELDS)
            writer.writeheader()
            writer.writerows(rows)
        return path

    def rows_for_status(self, status):
        return self.conn.execute(
            "SELECT COUNT(*) FROM staged_items WHERE status=?", (status,)
        ).fetchone()[0]

    def test_gtin_check_digits(self):
        self.assertTrue(valid_gtin("4006381333931"))
        self.assertFalse(valid_gtin("4006381333932"))
        self.assertFalse(valid_gtin("1234567"))
        self.assertFalse(valid_gtin("12 345 678"))

    def test_nonfood_demo_and_food_blocked(self):
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", DEMO)
        self.assertEqual(self.rows_for_status("PENDING_REVIEW"), 5)
        self.assertEqual(self.rows_for_status("OUT_OF_SCOPE"), 1)
        self.assertEqual(self.conn.execute("SELECT COUNT(*) FROM staged_items").fetchone()[0], 6)

    def test_wrong_category_food_terms_blocked(self):
        item = self.rows()[0].copy()
        item["external_id"] = "F999"
        item["product_name"] = "Keripik kentang"
        item["category_code"] = "SPARE_PARTS"
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", self.make_csv([item]))
        self.assertEqual(self.rows_for_status("FOOD_REVIEW"), 1)

    def test_source_license_hold(self):
        item = self.rows()[0].copy()
        item["external_id"] = "EXT001"
        stage_file(self.conn, "ID_EKATALOG_ZENODO_2026", self.make_csv([item]))
        self.assertEqual(self.rows_for_status("HOLD_LICENSE"), 1)
        item_id = self.conn.execute("SELECT id FROM staged_items").fetchone()[0]
        with self.assertRaises(ValueError):
            review(self.conn, item_id, "APPROVE", "tester", "checked record")

    def test_invalid_barcode_quarantined(self):
        item = self.rows()[0].copy()
        item["external_id"] = "BAD1"
        item["barcode"] = "4006381333932"
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", self.make_csv([item]))
        self.assertEqual(self.rows_for_status("INVALID_BARCODE"), 1)

    def test_exact_duplicate_detected(self):
        items = self.rows()[:1] * 2
        items = [dict(items[0]), dict(items[1])]
        items[1]["external_id"] = "B999"
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", self.make_csv(items))
        self.assertEqual(self.rows_for_status("PENDING_REVIEW"), 1)
        self.assertEqual(self.rows_for_status("POSSIBLE_DUPLICATE"), 1)

    def test_idempotent_reimport(self):
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", DEMO)
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", DEMO)
        self.assertEqual(self.conn.execute("SELECT COUNT(*) FROM staged_items").fetchone()[0], 6)

    def test_changed_external_id_refused_and_rollback(self):
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", self.make_csv(self.rows()[:1]))
        old = self.rows()[0].copy()
        old["product_name"] = "Changed product"
        new = self.rows()[1].copy()
        with self.assertRaises(ValueError):
            stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", self.make_csv([new, old]))
        self.assertEqual(self.conn.execute("SELECT COUNT(*) FROM staged_items").fetchone()[0], 1)

    def test_approve_then_export_has_no_fake_price_or_stock(self):
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", DEMO)
        item_id = self.conn.execute(
            "SELECT id FROM staged_items WHERE external_id='B001'"
        ).fetchone()[0]
        review(self.conn, item_id, "APPROVE", "tester", "verified synthetic fixture")
        out = self.root / "candidates.csv"
        export_candidates(self.conn, out)
        with out.open(encoding="utf-8", newline="") as f:
            exported = list(csv.DictReader(f))
        self.assertEqual(len(exported), 1)
        self.assertEqual(exported[0]["sku"], "")
        self.assertEqual(exported[0]["sale_price_idr"], "")
        self.assertEqual(exported[0]["opening_qty"], "")
        self.assertEqual(exported[0]["source_id"], "DEMO_SYNTHETIC_NONFOOD")

    def test_export_before_review_empty(self):
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", DEMO)
        out = self.root / "candidates.csv"
        export_candidates(self.conn, out)
        with out.open(encoding="utf-8", newline="") as f:
            self.assertEqual(len(list(csv.DictReader(f))), 0)

    def test_review_requires_reviewer_note(self):
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", DEMO)
        item_id = self.conn.execute(
            "SELECT id FROM staged_items WHERE external_id='B001'"
        ).fetchone()[0]
        with self.assertRaises(ValueError):
            review(self.conn, item_id, "APPROVE", "", "")

    def test_barcode_duplicate(self):
        a, b = [dict(x) for x in self.rows()[:2]]
        a["barcode"] = "4006381333931"
        b["barcode"] = "4006381333931"
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", self.make_csv([a, b]))
        self.assertEqual(self.rows_for_status("POSSIBLE_DUPLICATE"), 1)

    def test_bad_csv_header_fails_without_import(self):
        bad = self.root / "bad.csv"
        bad.write_text("name,price\nfoo,10\n", encoding="utf-8")
        with self.assertRaises(ValueError):
            stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", bad)
        self.assertEqual(self.conn.execute("SELECT COUNT(*) FROM staged_items").fetchone()[0], 0)

    def test_duplicate_external_id_in_batch_fails(self):
        a = dict(self.rows()[0])
        with self.assertRaises(ValueError):
            stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD", self.make_csv([a, a]))
        self.assertEqual(self.conn.execute("SELECT COUNT(*) FROM staged_items").fetchone()[0], 0)

    def test_source_without_registry_entry_refused(self):
        with self.assertRaises(ValueError):
            stage_file(self.conn, "UNKNOWN_SOURCE", DEMO)


    def test_source_review_lifts_license_hold_and_records_evidence(self):
        item = self.rows()[0].copy()
        item["external_id"] = "M0001"
        stage_file(self.conn, "ID_MENDELEY_MOTORPARTS_2026", self.make_csv([item]))
        self.assertEqual(self.rows_for_status("HOLD_LICENSE"), 1)
        with self.assertRaises(ValueError):
            review_source(self.conn, "ID_MENDELEY_MOTORPARTS_2026", "APPROVED",
                          "reviewer", "checked", "")
        review_source(self.conn, "ID_MENDELEY_MOTORPARTS_2026", "APPROVED",
                      "reviewer", "evidence reviewed", "https://example.org/evidence")
        self.assertEqual(self.rows_for_status("PENDING_REVIEW"), 1)
        self.assertEqual(self.conn.execute(
            "SELECT COUNT(*) FROM source_review_log"
        ).fetchone()[0], 1)

    def test_source_revocation_blocks_prior_item_approval(self):
        item = self.rows()[0].copy()
        item["external_id"] = "M0002"
        stage_file(self.conn, "ID_MENDELEY_MOTORPARTS_2026", self.make_csv([item]))
        review_source(self.conn, "ID_MENDELEY_MOTORPARTS_2026", "APPROVED",
                      "reviewer", "checked", "https://example.org/license")
        item_id = self.conn.execute("SELECT id FROM staged_items").fetchone()[0]
        review(self.conn, item_id, "APPROVE", "reviewer", "checked identity")
        review_source(self.conn, "ID_MENDELEY_MOTORPARTS_2026", "REJECTED",
                      "reviewer", "permission revoked", "")
        self.assertEqual(self.rows_for_status("HOLD_LICENSE"), 1)
        out = self.root / "safe.csv"
        export_candidates(self.conn, out)
        with out.open(encoding="utf-8", newline="") as f:
            self.assertEqual(len(list(csv.DictReader(f))), 0)


    def test_milk_color_is_not_food_and_milk_drink_is(self):
        example = self.rows()[0].copy()
        example["external_id"] = "OVENCOLOR"
        example["category_code"] = "ELECTRONICS_GENERAL"
        example["product_name"] = "Oven listrik 12 liter - Putih Susu"
        drink = self.rows()[0].copy()
        drink["external_id"] = "MILK"
        drink["category_code"] = "ELECTRONICS_GENERAL"
        drink["product_name"] = "Susu UHT rasa vanila"
        stage_file(self.conn, "DEMO_SYNTHETIC_NONFOOD",
                   self.make_csv([example, drink]))
        self.assertEqual(self.rows_for_status("PENDING_REVIEW"), 1)
        self.assertEqual(self.rows_for_status("FOOD_REVIEW"), 1)



if __name__ == "__main__":
    unittest.main()
