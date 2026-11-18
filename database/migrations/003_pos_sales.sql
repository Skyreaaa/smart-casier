-- Smart Casier POS sales. Staging migration 002 is a DIFFERENT SQLite file.
PRAGMA foreign_keys=ON;
BEGIN;
CREATE TABLE sales(
  sale_id TEXT PRIMARY KEY,
  receipt_no TEXT NOT NULL UNIQUE,
  request_hash TEXT NOT NULL,
  total_idr INTEGER NOT NULL CHECK(total_idr>=0),
  paid_idr INTEGER NOT NULL CHECK(paid_idr>=0),
  change_idr INTEGER NOT NULL CHECK(change_idr>=0),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CHECK(paid_idr=total_idr+change_idr)
);
CREATE TABLE sale_lines(
  line_id TEXT PRIMARY KEY,
  sale_id TEXT NOT NULL REFERENCES sales(sale_id) ON DELETE RESTRICT,
  variant_id TEXT NOT NULL REFERENCES variants(id) ON DELETE RESTRICT,
  sku_snapshot TEXT NOT NULL,
  product_snapshot TEXT NOT NULL,
  unit_code TEXT NOT NULL REFERENCES units(code),
  quantity_milli INTEGER NOT NULL CHECK(quantity_milli>0),
  unit_price_idr INTEGER NOT NULL CHECK(unit_price_idr>=0),
  line_total_idr INTEGER NOT NULL CHECK(line_total_idr>=0),
  UNIQUE(sale_id,variant_id)
);
CREATE INDEX idx_sale_lines_sale ON sale_lines(sale_id);
CREATE INDEX idx_sales_created ON sales(created_at);
PRAGMA user_version=3;
COMMIT;
