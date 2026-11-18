-- v5 operational POS migration. Legacy sales retain NULL operator_id for honest history.
PRAGMA foreign_keys=ON;
BEGIN;
ALTER TABLE sales ADD COLUMN operator_id TEXT REFERENCES app_users(id) ON DELETE RESTRICT;
CREATE INDEX idx_sales_operator_id ON sales(operator_id);
PRAGMA user_version=5;
COMMIT;
