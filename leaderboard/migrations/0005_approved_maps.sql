-- Approved (osu! status 2) maps award pp like Ranked (1). Scores on them were previously rejected rather
-- than stored, so existing totals are unaffected; clients resubmit them on their next sync.
DROP VIEW calculated_totals;
CREATE VIEW calculated_totals AS
  SELECT b.ruleset,b.user_id,b.generation,SUM(b.pp*w.weight) AS pp
  FROM (
    SELECT s.ruleset,s.user_id,s.generation,s.pp,
      ROW_NUMBER() OVER(PARTITION BY s.ruleset,s.user_id,s.generation ORDER BY s.pp DESC,s.played_at,s.map_id) AS position
    FROM scores s JOIN maps m ON m.id=s.map_id
    WHERE m.mode=0 AND m.md5=s.md5
    AND (m.ranked IN (1,2) OR EXISTS(SELECT 1 FROM whitelisted_sets WHERE ruleset=s.ruleset AND set_id=m.set_id))
  ) b JOIN weights w USING(position) GROUP BY b.ruleset,b.user_id,b.generation;
