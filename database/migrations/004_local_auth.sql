-- Operational POS migration v4: local account authorization.
-- Does not modify sales, stock movements or external dataset staging.
PRAGMA foreign_keys=ON;
BEGIN;
CREATE TABLE app_users(
    id TEXT PRIMARY KEY,
    username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    role TEXT NOT NULL CHECK(role IN ('ADMIN','CASHIER')),
    salt BLOB NOT NULL CHECK(length(salt)=32),
    password_hash BLOB NOT NULL CHECK(length(password_hash)=32),
    hash_iterations INTEGER NOT NULL CHECK(hash_iterations>=200000),
    active INTEGER NOT NULL DEFAULT 1 CHECK(active IN (0,1)),
    failed_attempts INTEGER NOT NULL DEFAULT 0 CHECK(failed_attempts>=0),
    lock_until_utc TEXT,
    created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE security_audit(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    actor_id TEXT REFERENCES app_users(id) ON DELETE SET NULL,
    action TEXT NOT NULL,
    target_user TEXT,
    created_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_security_audit_at ON security_audit(created_at_utc);
PRAGMA user_version=4;
COMMIT;
