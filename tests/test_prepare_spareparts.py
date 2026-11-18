import csv
import json
import tempfile
import unittest
from pathlib import Path

from tools.prepare_spareparts import transform
from tools.stage_dataset import init, connect, stage_file


class SparePartAdapterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.dir = Path(self.temp.name)
        self.csv = self.dir / "parts.csv"
        self.out = self.dir / "candidates.csv"
        with self.csv.open("w", encoding="utf-8", newline="") as file:
            writer = csv.writer(file)
            writer.writerow(("PartCode", "PartName", "Group", "CustomerName",
                             "CustomerAddress", "RetailPrice", "Brand"))
            writer.writerow(("0012", "Kampas rem demo", "Brake system", "Private Name",
                             "Private Address", "189999", "Test brand"))
            writer.writerow(("0025", "Baut M8 demo", "Fasteners", "Another Name",
                             "Another Address", "75000", ""))
            writer.writerow(("0012", "Kampas rem demo", "Brake system", "Private Name",
                             "Private Address", "189999", "Test brand"))

    def tearDown(self):
        self.temp.cleanup()

    def parse_output(self):
        with self.out.open(encoding="utf-8", newline="") as f:
            return list(csv.DictReader(f))

    def test_only_explicit_part_columns_exported(self):
        result = transform(self.csv, self.out, "PartCode", "PartName", "Group", "Brand")
        self.assertEqual(result["emitted"], 2)
        self.assertEqual(result["identical_duplicate"], 1)
        rows = self.parse_output()
        self.assertEqual(rows[0]["external_id"], "0012")
        self.assertEqual(rows[0]["category_code"], "SPARE_PARTS")
        self.assertEqual(rows[0]["unit_code"], "PCS")
        self.assertEqual(rows[0]["barcode"], "")
        self.assertNotIn("RetailPrice", rows[0])
        self.assertNotIn("CustomerName", rows[0])
        self.assertNotIn("Private Name", self.out.read_text(encoding="utf-8"))
        self.assertNotIn("189999", self.out.read_text(encoding="utf-8"))
        self.assertEqual(json.loads(rows[0]["attributes_json"])["original_group"], "Brake system")

    def test_kept_in_license_hold_until_source_approved(self):
        transform(self.csv, self.out, "PartCode", "PartName")
        db = self.dir / "stage.db"
        init(db)
        with connect(db) as conn:
            stage_file(conn, "ID_MENDELEY_MOTORPARTS_2026", self.out)
            self.assertEqual(conn.execute(
                "SELECT COUNT(*) FROM staged_items WHERE status='HOLD_LICENSE'"
            ).fetchone()[0], 2)

    def test_missing_mapping_column_rejected(self):
        with self.assertRaises(ValueError):
            transform(self.csv, self.out, "NotThere", "PartName")
        self.assertFalse(self.out.exists())

    def test_conflicting_same_part_id_rejected(self):
        with self.csv.open("a", encoding="utf-8") as file:
            file.write("0012,Different part,Other,Secret,address,2,brand\n")
        with self.assertRaises(ValueError):
            transform(self.csv, self.out, "PartCode", "PartName")
        self.assertFalse(self.out.exists())

    def test_id_and_name_must_differ(self):
        with self.assertRaises(ValueError):
            transform(self.csv, self.out, "PartCode", "PartCode")

    def test_limit_validation_and_no_overwrite(self):
        with self.assertRaises(ValueError):
            transform(self.csv, self.out, "PartCode", "PartName", limit=5001)
        transform(self.csv, self.out, "PartCode", "PartName")
        with self.assertRaises(ValueError):
            transform(self.csv, self.out, "PartCode", "PartName")


if __name__ == "__main__":
    unittest.main()
