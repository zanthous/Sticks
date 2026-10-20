-- osu! user IDs, not usernames. Also supports banning an account before its first login.
CREATE TABLE banned_users (user_id INTEGER PRIMARY KEY CHECK(user_id > 0));

-- Owner-only D1 console commands (replace 123456 with the osu! user ID):
-- Ban:   INSERT OR IGNORE INTO banned_users(user_id) VALUES (123456);
-- Unban: DELETE FROM banned_users WHERE user_id = 123456;
-- Scores are retained. Public rankings exclude banned users without changing local play.
