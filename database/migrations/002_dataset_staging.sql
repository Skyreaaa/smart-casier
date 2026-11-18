-- This is a SEPARATE staging database. Do not run against the operational POS DB.
PRAGMA foreign_keys=ON;
BEGIN;
CREATE TABLE dataset_sources(
 id TEXT PRIMARY KEY, title TEXT NOT NULL, url TEXT NOT NULL,
 license_name TEXT NOT NULL, review_status TEXT NOT NULL
 CHECK(review_status IN ('APPROVED','PENDING','REJECTED')),
 scope TEXT NOT NULL, note TEXT NOT NULL
);
CREATE TABLE staged_items(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 source_id TEXT NOT NULL REFERENCES dataset_sources(id),
 external_id TEXT NOT NULL,
 product_name TEXT NOT NULL,
 brand TEXT NOT NULL,
 category_code TEXT NOT NULL,
 variant TEXT NOT NULL,
 unit_code TEXT NOT NULL,
 barcode TEXT NOT NULL,
 attributes_json TEXT NOT NULL CHECK(json_valid(attributes_json)),
 product_url TEXT NOT NULL,
 fingerprint TEXT NOT NULL,
 payload_hash TEXT NOT NULL,
 status TEXT NOT NULL CHECK(status IN
 ('PENDING_REVIEW','HOLD_LICENSE','OUT_OF_SCOPE','INVALID_UNIT',
  'INVALID_BARCODE','FOOD_REVIEW','POSSIBLE_DUPLICATE','APPROVED','REJECTED')),
 reason TEXT NOT NULL DEFAULT '',
 reviewer TEXT, review_note TEXT, reviewed_at TEXT,
 created_at TEXT NOT NULL,
 UNIQUE(source_id,external_id)
);
CREATE INDEX idx_stage_barcode ON staged_items(barcode);
CREATE INDEX idx_stage_fingerprint ON staged_items(fingerprint);
CREATE INDEX idx_stage_status ON staged_items(status);
CREATE TABLE source_review_log(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 source_id TEXT NOT NULL REFERENCES dataset_sources(id),
 decision TEXT NOT NULL CHECK(decision IN ('APPROVED','REJECTED')),
 reviewer TEXT NOT NULL,note TEXT NOT NULL,evidence_url TEXT NOT NULL,
 reviewed_at TEXT NOT NULL
);
PRAGMA user_version=2;
COMMIT;
