CREATE TABLE users (id INTEGER PRIMARY KEY, username TEXT NOT NULL);
CREATE TABLE whitelisted_sets (
  ruleset TEXT NOT NULL, set_id INTEGER NOT NULL CHECK (set_id > 0),
  PRIMARY KEY(ruleset, set_id)
) WITHOUT ROWID;
CREATE TABLE maps (
  id INTEGER PRIMARY KEY, set_id INTEGER NOT NULL, md5 BLOB NOT NULL CHECK(length(md5) = 16),
  mode INTEGER NOT NULL, ranked INTEGER NOT NULL, checked_at INTEGER NOT NULL
);
CREATE TABLE scores (
  ruleset TEXT NOT NULL,
  user_id INTEGER NOT NULL REFERENCES users(id), map_id INTEGER NOT NULL REFERENCES maps(id),
  generation INTEGER NOT NULL,
  score_id BLOB NOT NULL CHECK(length(score_id) = 16), md5 BLOB NOT NULL CHECK(length(md5) = 16),
  played_at INTEGER NOT NULL, difficulty_version INTEGER NOT NULL, mods INTEGER NOT NULL,
  rate REAL NOT NULL, pp REAL NOT NULL CHECK(pp >= 0), accuracy REAL NOT NULL CHECK(accuracy BETWEEN 0 AND 1),
  combo INTEGER NOT NULL, total_score INTEGER NOT NULL, activation REAL NOT NULL,
  judgements BLOB NOT NULL CHECK(length(judgements) BETWEEN 4 AND 256 AND length(judgements) % 4 = 0),
  PRIMARY KEY(ruleset, user_id, generation, map_id)
) WITHOUT ROWID;
CREATE INDEX scores_pp ON scores(ruleset, user_id, generation, pp DESC, played_at, map_id);
CREATE INDEX scores_map ON scores(map_id, ruleset, user_id);
CREATE TABLE totals (
  ruleset TEXT NOT NULL,
  user_id INTEGER NOT NULL REFERENCES users(id), generation INTEGER NOT NULL,
  pp REAL NOT NULL, PRIMARY KEY(ruleset, user_id, generation)
) WITHOUT ROWID;
CREATE INDEX totals_rank ON totals(ruleset, generation, pp DESC, user_id);
CREATE TABLE sessions (token_hash TEXT PRIMARY KEY, user_id INTEGER NOT NULL REFERENCES users(id), expires INTEGER NOT NULL) WITHOUT ROWID;
CREATE INDEX sessions_expiry ON sessions(expires);
CREATE TABLE logins (
  id TEXT PRIMARY KEY, user_id INTEGER NOT NULL, challenge TEXT NOT NULL,
  state TEXT UNIQUE, browser_hash TEXT, verified INTEGER NOT NULL DEFAULT 0, expires INTEGER NOT NULL
) WITHOUT ROWID;
CREATE INDEX logins_expiry ON logins(expires);
CREATE TABLE limits (key TEXT PRIMARY KEY, count INTEGER NOT NULL, expires INTEGER NOT NULL) WITHOUT ROWID;
CREATE TABLE service_tokens (key TEXT PRIMARY KEY, token TEXT NOT NULL, expires INTEGER NOT NULL) WITHOUT ROWID;

CREATE TABLE weights (position INTEGER PRIMARY KEY, weight REAL NOT NULL);
INSERT INTO weights WITH RECURSIVE w(n,value) AS
  (VALUES(1,1.0) UNION ALL SELECT n+1,value*0.95 FROM w WHERE n<100) SELECT n,value FROM w;
CREATE VIEW calculated_totals AS
  SELECT b.ruleset,b.user_id,b.generation,SUM(b.pp*w.weight) AS pp
  FROM (
    SELECT s.ruleset,s.user_id,s.generation,s.pp,
      ROW_NUMBER() OVER(PARTITION BY s.ruleset,s.user_id,s.generation ORDER BY s.pp DESC,s.played_at,s.map_id) AS position
    FROM scores s JOIN maps m ON m.id=s.map_id
    WHERE m.mode=0 AND m.md5=s.md5
    AND (m.ranked=1 OR EXISTS(SELECT 1 FROM whitelisted_sets WHERE ruleset=s.ruleset AND set_id=m.set_id))
  ) b JOIN weights w USING(position) GROUP BY b.ruleset,b.user_id,b.generation;

-- Owner-only D1 console commands; no publicly writable admin endpoint:
-- INSERT OR IGNORE INTO whitelisted_sets(ruleset,set_id) VALUES ('sticks',YOUR_SET_ID);
-- DELETE FROM whitelisted_sets WHERE ruleset='sticks' AND set_id=YOUR_SET_ID;
-- These rare policy changes rebuild cached totals, including players who are offline.
CREATE TRIGGER whitelist_added AFTER INSERT ON whitelisted_sets BEGIN
  UPDATE totals SET pp=COALESCE((SELECT c.pp FROM calculated_totals c
    WHERE c.ruleset=totals.ruleset AND c.user_id=totals.user_id AND c.generation=totals.generation),0)
    WHERE ruleset=NEW.ruleset;
END;
CREATE TRIGGER whitelist_removed AFTER DELETE ON whitelisted_sets BEGIN
  UPDATE totals SET pp=COALESCE((SELECT c.pp FROM calculated_totals c
    WHERE c.ruleset=totals.ruleset AND c.user_id=totals.user_id AND c.generation=totals.generation),0)
    WHERE ruleset=OLD.ruleset;
END;
CREATE TRIGGER map_changed AFTER UPDATE OF md5,mode,ranked,set_id ON maps
WHEN NEW.md5!=OLD.md5 OR NEW.mode!=OLD.mode OR NEW.ranked!=OLD.ranked OR NEW.set_id!=OLD.set_id BEGIN
  UPDATE totals SET pp=COALESCE((SELECT c.pp FROM calculated_totals c
    WHERE c.ruleset=totals.ruleset AND c.user_id=totals.user_id AND c.generation=totals.generation),0)
    WHERE EXISTS(SELECT 1 FROM scores s WHERE s.map_id=NEW.id AND s.ruleset=totals.ruleset AND s.user_id=totals.user_id);
END;
