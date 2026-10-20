-- Shared across Worker instances and all rulesets using this database. Times are milliseconds.
CREATE TABLE osu_api_state (
  id INTEGER PRIMARY KEY CHECK(id = 1),
  next_request_at INTEGER NOT NULL DEFAULT 0,
  lease_owner TEXT,
  lease_until INTEGER NOT NULL DEFAULT 0,
  blocked_until INTEGER NOT NULL DEFAULT 0,
  failures INTEGER NOT NULL DEFAULT 0
);
INSERT INTO osu_api_state(id) VALUES(1);

-- A lookup has one owner. Failed/missing results also impose a cooldown.
CREATE TABLE osu_lookup_locks (
  key TEXT PRIMARY KEY,
  owner TEXT NOT NULL,
  retry_at INTEGER NOT NULL
) WITHOUT ROWID;
