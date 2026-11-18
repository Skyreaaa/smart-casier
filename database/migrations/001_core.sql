PRAGMA foreign_keys=ON;
BEGIN;
-- Stock qty = integer milli-units: 1 PCS=1000; 2.5 meters=2500.
-- Prices use integer IDR, never floating point.
CREATE TABLE units(
  code TEXT PRIMARY KEY, label TEXT NOT NULL,
  fractional_allowed INTEGER NOT NULL CHECK(fractional_allowed IN (0,1))
);
INSERT INTO units VALUES('PCS','piece',0),('M','meter',1),('TXN','transaction',0);
CREATE TABLE categories(
  id TEXT PRIMARY KEY, code TEXT NOT NULL UNIQUE COLLATE NOCASE,
  name TEXT NOT NULL, parent_id TEXT REFERENCES categories(id) ON DELETE RESTRICT
);
CREATE TABLE products(
  id TEXT PRIMARY KEY, product_key TEXT NOT NULL UNIQUE COLLATE NOCASE,
  category_id TEXT NOT NULL REFERENCES categories(id),
  name TEXT NOT NULL, product_type TEXT NOT NULL
    CHECK(product_type IN ('PHYSICAL','DIGITAL','SERVICE')),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN(0,1)),
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE variants(
  id TEXT PRIMARY KEY, product_id TEXT NOT NULL REFERENCES products(id),
  sku TEXT NOT NULL UNIQUE COLLATE NOCASE,
  label TEXT NOT NULL DEFAULT '',
  unit_code TEXT NOT NULL REFERENCES units(code),
  sale_price_idr INTEGER NOT NULL CHECK(sale_price_idr>=0),
  track_stock INTEGER NOT NULL CHECK(track_stock IN(0,1)),
  low_stock_threshold_milli INTEGER NOT NULL DEFAULT 3000
    CHECK(low_stock_threshold_milli>=0),
  attributes_json TEXT NOT NULL DEFAULT '{}'
    CHECK(json_valid(attributes_json) AND json_type(attributes_json)='object'),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK(is_active IN(0,1)),
  CHECK(track_stock=1 OR low_stock_threshold_milli=0)
);
CREATE TABLE barcodes(
  code TEXT PRIMARY KEY, variant_id TEXT NOT NULL REFERENCES variants(id)
);
CREATE TABLE variant_aliases(
  id TEXT PRIMARY KEY, variant_id TEXT NOT NULL REFERENCES variants(id),
  alias TEXT NOT NULL COLLATE NOCASE, UNIQUE(variant_id,alias)
);
CREATE TABLE stock_movements(
  id TEXT PRIMARY KEY, variant_id TEXT NOT NULL REFERENCES variants(id),
  movement_type TEXT NOT NULL CHECK(movement_type IN
    ('OPENING','RECEIPT','SALE','RETURN','ADJUSTMENT','WASTE')),
  delta_qty_milli INTEGER NOT NULL CHECK(delta_qty_milli<>0),
  source_ref TEXT, note TEXT,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_movements_variant ON stock_movements(variant_id);
CREATE INDEX idx_alias ON variant_aliases(alias);
CREATE INDEX idx_products_category ON products(category_id);
CREATE TRIGGER movement_guard BEFORE INSERT ON stock_movements
BEGIN
 SELECT CASE WHEN (SELECT track_stock FROM variants WHERE id=NEW.variant_id)!=1
   THEN RAISE(ABORT,'stock not tracked') END;
 SELECT CASE WHEN (
   SELECT u.fractional_allowed FROM variants v JOIN units u ON v.unit_code=u.code
   WHERE v.id=NEW.variant_id
 )=0 AND NEW.delta_qty_milli%1000!=0
   THEN RAISE(ABORT,'fractional pieces not allowed') END;
 SELECT CASE WHEN COALESCE(
   (SELECT SUM(delta_qty_milli) FROM stock_movements
    WHERE variant_id=NEW.variant_id),0)+NEW.delta_qty_milli<0
   THEN RAISE(ABORT,'negative stock blocked') END;
END;
CREATE TRIGGER movement_no_update BEFORE UPDATE ON stock_movements
BEGIN SELECT RAISE(ABORT,'append-only stock ledger'); END;
CREATE TRIGGER movement_no_delete BEFORE DELETE ON stock_movements
BEGIN SELECT RAISE(ABORT,'append-only stock ledger'); END;
CREATE VIEW v_inventory_status AS
 SELECT v.sku,v.id AS variant_id,p.name AS product_name,v.label,
        v.unit_code,v.track_stock,v.low_stock_threshold_milli,
        COALESCE(SUM(m.delta_qty_milli),0) AS on_hand_milli,
        CASE WHEN v.track_stock=0 THEN 'UNTRACKED'
             WHEN COALESCE(SUM(m.delta_qty_milli),0)<=0 THEN 'OUT'
             WHEN COALESCE(SUM(m.delta_qty_milli),0)<v.low_stock_threshold_milli THEN 'LOW'
             ELSE 'OK' END AS stock_status
 FROM variants v JOIN products p ON p.id=v.product_id
 LEFT JOIN stock_movements m ON m.variant_id=v.id GROUP BY v.id;
PRAGMA user_version=1;
COMMIT;
