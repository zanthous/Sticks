-- Titles are cached only when a score submission already verifies its source map.
CREATE TABLE map_details (
  map_id INTEGER PRIMARY KEY REFERENCES maps(id),
  title TEXT NOT NULL, artist TEXT NOT NULL, difficulty TEXT NOT NULL
);

CREATE TABLE replays (
  object_key TEXT PRIMARY KEY,
  ruleset TEXT NOT NULL, user_id INTEGER NOT NULL, generation INTEGER NOT NULL,
  map_id INTEGER NOT NULL, score_id BLOB NOT NULL,
  state TEXT NOT NULL CHECK(state IN ('pending','ready','deleting')),
  bytes INTEGER NOT NULL CHECK(bytes>0), sha256 TEXT NOT NULL,
  created_at INTEGER NOT NULL, lease_until INTEGER NOT NULL,
  UNIQUE(ruleset,user_id,generation,map_id,score_id)
) WITHOUT ROWID;
CREATE INDEX replays_owner ON replays(ruleset,user_id);
CREATE INDEX replays_state ON replays(state,lease_until);
CREATE TABLE replay_storage (
  id INTEGER PRIMARY KEY CHECK(id=1), bytes INTEGER NOT NULL CHECK(bytes>=0)
);
INSERT INTO replay_storage VALUES(1,0);
CREATE TRIGGER replay_added AFTER INSERT ON replays BEGIN
  UPDATE replay_storage SET bytes=bytes+NEW.bytes WHERE id=1;
END;
CREATE TRIGGER replay_removed AFTER DELETE ON replays BEGIN
  UPDATE replay_storage SET bytes=bytes-OLD.bytes WHERE id=1;
END;

-- Durable cleanup queue: failed R2 deletions and interrupted requests are retried.
CREATE TABLE replay_cleanup_users (
  ruleset TEXT NOT NULL, user_id INTEGER NOT NULL, revision INTEGER NOT NULL DEFAULT 1,
  PRIMARY KEY(ruleset,user_id)
) WITHOUT ROWID;
CREATE TABLE replay_settings (
  ruleset TEXT PRIMARY KEY, generation INTEGER NOT NULL, retained INTEGER NOT NULL
) WITHOUT ROWID;
CREATE TABLE replay_sweep (id INTEGER PRIMARY KEY CHECK(id=1), cursor TEXT);
INSERT INTO replay_sweep VALUES(1,NULL);
CREATE TRIGGER replay_score_inserted AFTER INSERT ON scores BEGIN
  INSERT INTO replay_cleanup_users(ruleset,user_id) VALUES(NEW.ruleset,NEW.user_id)
    ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1;
END;
CREATE TRIGGER replay_score_updated AFTER UPDATE ON scores BEGIN
  -- Also invalidate when a client reuses the same score ID with different data.
  UPDATE replays SET state='deleting' WHERE ruleset=OLD.ruleset AND user_id=OLD.user_id
    AND generation=OLD.generation AND map_id=OLD.map_id AND score_id=OLD.score_id;
  INSERT INTO replay_cleanup_users(ruleset,user_id) VALUES(NEW.ruleset,NEW.user_id)
    ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1;
END;
CREATE TRIGGER replay_whitelist_added AFTER INSERT ON whitelisted_sets BEGIN
  INSERT INTO replay_cleanup_users(ruleset,user_id)
    SELECT s.ruleset,s.user_id FROM scores s JOIN maps m ON m.id=s.map_id
    WHERE s.ruleset=NEW.ruleset AND m.set_id=NEW.set_id GROUP BY s.ruleset,s.user_id
    ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1;
END;
CREATE TRIGGER replay_score_deleted AFTER DELETE ON scores BEGIN
  INSERT INTO replay_cleanup_users(ruleset,user_id) VALUES(OLD.ruleset,OLD.user_id)
    ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1;
END;
CREATE TRIGGER replay_map_changed AFTER UPDATE OF md5,mode,ranked,set_id ON maps
WHEN NEW.md5!=OLD.md5 OR NEW.mode!=OLD.mode OR NEW.ranked!=OLD.ranked OR NEW.set_id!=OLD.set_id BEGIN
  INSERT INTO replay_cleanup_users(ruleset,user_id)
    SELECT ruleset,user_id FROM scores WHERE map_id=NEW.id GROUP BY ruleset,user_id
    ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1;
END;
CREATE TRIGGER replay_whitelist_removed AFTER DELETE ON whitelisted_sets BEGIN
  INSERT INTO replay_cleanup_users(ruleset,user_id)
    SELECT s.ruleset,s.user_id FROM scores s JOIN maps m ON m.id=s.map_id
    WHERE s.ruleset=OLD.ruleset AND m.set_id=OLD.set_id GROUP BY s.ruleset,s.user_id
    ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1;
END;
CREATE TRIGGER replay_user_banned AFTER INSERT ON banned_users BEGIN
  INSERT INTO replay_cleanup_users(ruleset,user_id)
    SELECT ruleset,user_id FROM replays WHERE user_id=NEW.user_id GROUP BY ruleset,user_id
    ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1;
END;
