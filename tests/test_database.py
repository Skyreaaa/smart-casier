import csv
import sqlite3
import tempfile
import unittest
import uuid
from pathlib import Path
from tools.import_catalog import FIELDS, validate, import_rows

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = (ROOT / "database/migrations/001_core.sql").read_text(encoding="utf-8")
DEMO = ROOT / "data/examples/catalog_demo.csv"


class DatabaseTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.db.execute("PRAGMA foreign_keys=ON")
        self.db.executescript(SCHEMA)

    def tearDown(self):
        self.db.close()

    def load(self):
        return import_rows(self.db, validate(DEMO))

    def stock(self, sku):
        return self.db.execute(
            "SELECT on_hand_milli,stock_status FROM v_inventory_status WHERE sku=?", (sku,)
        ).fetchone()

    def move(self, sku, amount):
        variant = self.db.execute("SELECT id FROM variants WHERE sku=?", (sku,)).fetchone()[0]
        self.db.execute(
            "INSERT INTO stock_movements(id,variant_id,movement_type,delta_qty_milli) "
            "VALUES(?,?,'SALE',?)", (uuid.uuid4().hex, variant, amount)
        )

    def test_migration_and_foreign_key(self):
        self.assertEqual(self.db.execute("PRAGMA user_version").fetchone()[0], 1)
        self.assertEqual(self.db.execute("PRAGMA foreign_key_check").fetchall(), [])

    def test_catalog_import(self):
        count = len(validate(DEMO))
        self.assertEqual(self.load(), count)
        self.assertEqual(self.db.execute("SELECT COUNT(*) FROM variants").fetchone()[0], count)

    def test_low_stock_strictly_below_three(self):
        self.load()
        self.assertEqual(self.stock("BAUT-M8X30"), (3000, "OK"))
        self.move("BAUT-M8X30", -1000)
        self.assertEqual(self.stock("BAUT-M8X30"), (2000, "LOW"))
        self.move("BAUT-M8X30", -2000)
        self.assertEqual(self.stock("BAUT-M8X30"), (0, "OUT"))

    def test_meter_quantity_exact(self):
        self.load()
        self.assertEqual(self.stock("KMY-MERAH"), (2550, "LOW"))
        self.assertEqual(self.stock("KMY-BIRU"), (12000, "OK"))

    def test_piece_cannot_be_fractional(self):
        self.load()
        with self.assertRaises(sqlite3.IntegrityError):
            self.move("BAUT-M8X30", -500)

    def test_negative_stock_blocked(self):
        self.load()
        with self.assertRaises(sqlite3.IntegrityError):
            self.move("BAUT-M8X30", -4000)

    def test_inventory_is_immutable(self):
        self.load()
        movement_id = self.db.execute("SELECT id FROM stock_movements LIMIT 1").fetchone()[0]
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE stock_movements SET note='changed' WHERE id=?", (movement_id,))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("DELETE FROM stock_movements WHERE id=?", (movement_id,))

    def test_digital_has_no_stock(self):
        self.load()
        self.assertEqual(self.stock("PULSA-10K"), (0, "UNTRACKED"))
        with self.assertRaises(sqlite3.IntegrityError):
            self.move("PULSA-10K", 1000)

    def test_duplicate_sku_rollback(self):
        self.load()
        prev = self.db.execute("SELECT COUNT(*) FROM stock_movements").fetchone()[0]
        with self.assertRaises(sqlite3.IntegrityError):
            self.load()
        self.assertEqual(self.db.execute("SELECT COUNT(*) FROM stock_movements").fetchone()[0], prev)

    def test_invalid_catalog_price_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            with DEMO.open(encoding="utf-8", newline="") as f:
                rows = list(csv.DictReader(f))
            rows[0]["sale_price_idr"] = "-10"
            invalid = Path(tmp) / "invalid.csv"
            with invalid.open("w", encoding="utf-8", newline="") as f:
                writer = csv.DictWriter(f, fieldnames=FIELDS)
                writer.writeheader()
                writer.writerows(rows)
            with self.assertRaises(ValueError):
                validate(invalid)

    def test_alias_and_barcode_index(self):
        self.load()
        self.assertGreater(self.db.execute("SELECT COUNT(*) FROM variant_aliases").fetchone()[0], 0)
        self.assertGreater(self.db.execute("SELECT COUNT(*) FROM barcodes").fetchone()[0], 0)


if __name__ == "__main__":
    unittest.main()
