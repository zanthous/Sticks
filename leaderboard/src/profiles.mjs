import { hex, integer } from "./policy.mjs";
import { fail, json, stmt } from "./http.mjs";

// Reused by reads, upload reservations/finalisation, and retention. A missing replay
// never promotes rank 101 into the top-100 storage allowance.
export const topScoresSql = `SELECT s.*,m.set_id
  FROM scores s JOIN maps m ON m.id=s.map_id
  WHERE s.ruleset=?1 AND s.user_id=?2 AND s.generation=?3
    AND m.mode=0 AND m.md5=s.md5
    AND (m.ranked IN (1,2) OR EXISTS(SELECT 1 FROM whitelisted_sets w WHERE w.ruleset=s.ruleset AND w.set_id=m.set_id))
    AND NOT EXISTS(SELECT 1 FROM banned_users b WHERE b.user_id=s.user_id)
  ORDER BY s.pp DESC,s.played_at,s.map_id LIMIT ?4`;

export function judgementStats(rule, blob) {
  const bytes = Uint8Array.from(blob);
  if (bytes.length % 4) fail(503, "Invalid stored judgement data");
  const view = new DataView(bytes.buffer);
  const judgements = Array.from({ length: bytes.length / 4 }, (_, i) => view.getUint32(i * 4, true));
  if (rule.id !== "sticks" || judgements.length !== 16)
    return { judgements, missCount: null, statistics: null };
  const group = i => ({ great: judgements[i], ok: judgements[i + 1], meh: judgements[i + 2], miss: judgements[i + 3] });
  return {
    judgements,
    // A missed head records BOTH timing and angle misses. Count it only once.
    missCount: judgements[3] + judgements[11],
    statistics: {
      timing: group(0), angle: group(4), click: group(8),
      tracking: { hit: judgements[12], miss: judgements[13] },
      tail: { hit: judgements[14], miss: judgements[15] },
    },
  };
}

export function scoreSummary(rule, row, rank) {
  return {
    rank, scoreId: hex(row.score_id), userId: row.user_id, beatmapId: row.map_id,
    beatmapSetId: row.set_id, sourceMd5: hex(row.md5), playedAt: row.played_at,
    generation: row.generation, difficultyVersion: row.difficulty_version,
    mods: row.mods, rate: row.rate, pp: row.pp,
    weightedPp: rank == null ? null : row.pp * 0.95 ** (rank - 1),
    accuracy: row.accuracy, maxCombo: row.combo, totalScore: row.total_score,
    activationThreshold: row.activation,
    title: row.title ?? null, artist: row.artist ?? null, difficulty: row.difficulty ?? null,
    ...judgementStats(rule, row.judgements),
  };
}

export const replayPath = (rule, userId, mapId, scoreId) =>
  `/v1/rulesets/${rule.id}/users/${userId}/scores/${mapId}/${scoreId}/replay`;

export async function getTopPlays(url, env, rule, userId, replayPolicy) {
  const offset = Number(url.searchParams.get("offset") ?? 0), limit = Number(url.searchParams.get("limit") ?? 50);
  if ([...url.searchParams.keys()].some(k => !["offset", "limit"].includes(k))
      || ["offset", "limit"].some(k => url.searchParams.getAll(k).length > 1)
      || !integer(offset, 0, 99) || !integer(limit, 1, 100))
    fail(400, "Invalid top-play page");
  const user = await stmt(env, `SELECT u.id,u.username,t.pp FROM users u
    LEFT JOIN totals t ON t.user_id=u.id AND t.ruleset=? AND t.generation=?
    WHERE u.id=? AND NOT EXISTS(SELECT 1 FROM banned_users WHERE user_id=u.id)`,
    rule.id, rule.generation, userId).first();
  if (!user) fail(404, "Player not found");
  // At most 100 indexed PBs. No upstream osu! requests, even with missing titles.
  const rows = await stmt(env, `WITH best AS (${topScoresSql})
    SELECT best.*,d.title,d.artist,d.difficulty,r.bytes AS replay_bytes,r.sha256 AS replay_sha256
    FROM best LEFT JOIN map_details d ON d.map_id=best.map_id
    LEFT JOIN replays r ON r.ruleset=best.ruleset AND r.user_id=best.user_id
      AND r.generation=best.generation AND r.map_id=best.map_id AND r.score_id=best.score_id AND r.state='ready'
    ORDER BY best.pp DESC,best.played_at,best.map_id`, rule.id, userId, rule.generation, 100).all();
  const entries = rows.results.slice(offset, Math.min(100, offset + limit)).map((row, i) => {
    const rank = offset + i + 1, eligible = rule.id === "sticks" && rank <= replayPolicy.retainedPerPlayer && !!env.REPLAYS;
    return { ...scoreSummary(rule, row, rank), replay: {
      eligible, available: eligible && row.replay_bytes != null,
      bytes: eligible ? row.replay_bytes ?? null : null, sha256: eligible ? row.replay_sha256 ?? null : null,
      downloadPath: eligible && row.replay_bytes != null ? replayPath(rule, userId, row.map_id, hex(row.score_id)) : null,
    } };
  });
  return json({ ruleset: rule.id, generation: rule.generation,
    player: { userId, username: user.username, pp: user.pp ?? 0 },
    total: rows.results.length, offset, entries,
    nextOffset: offset + entries.length < rows.results.length ? offset + entries.length : null });
}
