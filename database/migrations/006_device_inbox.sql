-- v6 device inbox: hardware-independent input protocol, not actual measurement certification.
PRAGMA foreign_keys=ON;
BEGIN;
CREATE TABLE device_events(
  event_id TEXT PRIMARY KEY,
  device_id TEXT NOT NULL CHECK(length(device_id) BETWEEN 2 AND 64),
  kind TEXT NOT NULL CHECK(kind IN ('BARCODE','LENGTH')),
  barcode TEXT,
  length_milli INTEGER,
  status TEXT NOT NULL DEFAULT 'PENDING' CHECK(status IN ('PENDING','ACKED','REJECTED')),
  resolved_sku TEXT,
  resolved_operator_id TEXT REFERENCES app_users(id) ON DELETE RESTRICT,
  created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  resolved_at_utc TEXT,
  CHECK(
    (kind='BARCODE' AND barcode IS NOT NULL AND length(barcode) BETWEEN 1 AND 100 AND length_milli IS NULL)
    OR (kind='LENGTH' AND barcode IS NULL AND length_milli BETWEEN 1 AND 100000)
  )
);
CREATE INDEX idx_device_events_pending ON device_events(status,created_at_utc);
PRAGMA user_version=6;
COMMIT;
