export class HttpError extends Error {
  constructor(status, message, retryAfter) { super(message); this.status = status; this.retryAfter = retryAfter; }
}
export const hex = bytes => Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, "0")).join("");
export const unhex = value => Uint8Array.from(value.match(/../g), s => parseInt(s, 16));
export const digest = async value => hex(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
export const random = () => hex(crypto.getRandomValues(new Uint8Array(32)));
export const integer = (value, min, max) => Number.isSafeInteger(value) && value >= min && value <= max;
const number = (value, min, max) => typeof value === "number" && Number.isFinite(value) && value >= min && value <= max;

export function resolveRuleset(env, id) {
  if (!/^[a-z][a-z0-9-]{0,31}$/.test(id) || !env.RULESETS || !Object.hasOwn(env.RULESETS, id))
    throw new HttpError(404, "Unknown ruleset");
  const rule = env.RULESETS[id];
  if (!rule || !integer(rule.generation, 1, 2147483647) || !integer(rule.difficultyVersion, 1, 2147483647)
      || !integer(rule.judgementCount, 1, 64)) throw new HttpError(503, "Ruleset policy not configured");
  return { id, generation: rule.generation, difficultyVersion: rule.difficultyVersion, judgementCount: rule.judgementCount };
}

const fields = ["scoreId", "userId", "beatmapId", "beatmapSetId", "sourceMd5", "playedAt", "generation", "difficultyVersion", "mods", "rate", "pp", "accuracy", "maxCombo", "totalScore", "activationThreshold", "judgements"];
export function validateScore(s, userId, rule, now = Date.now()) {
  if (!s || typeof s !== "object" || Object.keys(s).some(key => !fields.includes(key))
      || !/^[a-f0-9]{32}$/.test(s.scoreId) || !/^[a-f0-9]{32}$/.test(s.sourceMd5)
      || s.userId !== userId || !integer(s.userId, 1, 2147483647)
      || !integer(s.beatmapId, 1, 2147483647) || !integer(s.beatmapSetId, 1, 2147483647)
      || s.generation !== rule.generation || s.difficultyVersion !== rule.difficultyVersion
      || !integer(s.playedAt, 1, now + 300000) || s.mods !== 0
      || s.rate !== 1 || !number(s.pp, 0, 100000) || !number(s.accuracy, 0, 1)
      || !integer(s.maxCombo, 0, 10000000) || !integer(s.totalScore, 0, Number.MAX_SAFE_INTEGER)
      || !number(s.activationThreshold, 0.01, 1)
      || !Array.isArray(s.judgements) || s.judgements.length !== rule.judgementCount || !s.judgements.every(n => integer(n, 0, 10000000)))
    throw new HttpError(400, "Unsupported score summary");
  return s;
}

// osu! statuses that award pp: 1 Ranked and 2 Approved. SQL predicates use `m.ranked IN (1,2)` to match.
export const awardsPP = ranked => ranked === 1 || ranked === 2;

export function eligible(score, map, whitelisted) {
  return map && map.mode === 0 && map.set_id === score.beatmapSetId
    && hex(map.md5) === score.sourceMd5 && (awardsPP(map.ranked) || whitelisted);
}

export function packJudgements(counts) {
  const bytes = new Uint8Array(counts.length * 4), view = new DataView(bytes.buffer);
  counts.forEach((n, i) => view.setUint32(i * 4, n, true));
  return bytes;
}

export const upsertScoreSql = `INSERT INTO scores
  (ruleset,user_id,map_id,generation,score_id,md5,played_at,difficulty_version,mods,rate,pp,accuracy,combo,total_score,activation,judgements)
  VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
  ON CONFLICT(ruleset,user_id,generation,map_id) DO UPDATE SET
  score_id=excluded.score_id, md5=excluded.md5, played_at=excluded.played_at, difficulty_version=excluded.difficulty_version,
  mods=excluded.mods, rate=excluded.rate, pp=excluded.pp, accuracy=excluded.accuracy, combo=excluded.combo,
  total_score=excluded.total_score, activation=excluded.activation, judgements=excluded.judgements
  WHERE excluded.md5 != scores.md5 OR excluded.pp > scores.pp OR (excluded.pp = scores.pp AND excluded.played_at < scores.played_at)`;

// No dependence on optional SQLite math extensions. Recompute inside the same D1 transaction as the PB upsert.
export const updateTotalSql = `INSERT INTO totals(ruleset,user_id,generation,pp)
  WITH RECURSIVE weights(n,w) AS (VALUES(1,1.0) UNION ALL SELECT n+1,w*0.95 FROM weights WHERE n<100),
  best AS (SELECT s.pp, ROW_NUMBER() OVER(ORDER BY s.pp DESC,s.played_at,s.map_id) AS n
    FROM scores s JOIN maps m ON m.id=s.map_id
    WHERE s.user_id=?1 AND s.generation=?2 AND s.ruleset=?3 AND m.mode=0 AND m.md5=s.md5
    AND (m.ranked IN (1,2) OR EXISTS(SELECT 1 FROM whitelisted_sets WHERE ruleset=s.ruleset AND set_id=m.set_id))
    ORDER BY s.pp DESC,s.played_at,s.map_id LIMIT 100)
  SELECT ?3,?1,?2,COALESCE(SUM(best.pp*weights.w),0) FROM best JOIN weights USING(n)
  WHERE true ON CONFLICT(ruleset,user_id,generation) DO UPDATE SET pp=excluded.pp`;

export function scoreValues(s, ruleset) {
  return [ruleset, s.userId, s.beatmapId, s.generation, unhex(s.scoreId), unhex(s.sourceMd5), s.playedAt,
    s.difficultyVersion, s.mods, s.rate, s.pp, s.accuracy, s.maxCombo, s.totalScore, s.activationThreshold, packJudgements(s.judgements)];
}
