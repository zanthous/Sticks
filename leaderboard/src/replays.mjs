import { HttpError, hex, integer, random, resolveRuleset, unhex } from "./policy.mjs";
import { authenticate, fail, json, limit, now, readBounded, stmt } from "./http.mjs";
import { replayPath, scoreSummary, topScoresSql } from "./profiles.mjs";

const MiB = 1024 * 1024;
// Sized for a 30-minute recording: ~60 frames/s plus extra frames at neutral/activation crossings,
// 30 bytes each, leaves the raw limit headroom for ~150 frames/s.
const defaults = {
  retainedPerPlayer: 100, maxDurationSeconds: 30 * 60, maxCompressedBytes: 4 * MiB, maxRawBytes: 8 * MiB,
  maxStoredBytes: 8 * 1024 * MiB, uploadsPerHour: 120, uploadsPerDay: 240,
  uploadBytesPerDay: 128 * MiB, globalUploadsPerDay: 2000,
};

export function replayPolicy(env) {
  const policy = { ...defaults, ...env.REPLAY_POLICY };
  for (const key of Object.keys(policy)) {
    if (!(key in defaults) || !integer(policy[key], 1, Number.MAX_SAFE_INTEGER))
      fail(503, "Invalid replay policy");
  }
  if (policy.retainedPerPlayer > 100 || policy.maxCompressedBytes > 8 * MiB || policy.maxRawBytes > 32 * MiB)
    fail(503, "Replay policy exceeds supported limits");
  return policy;
}

async function selectedScore(env, rule, userId, mapId, scoreId, cap) {
  return stmt(env, `WITH best AS (${topScoresSql}) SELECT best.*,u.username,d.title,d.artist,d.difficulty
    FROM best JOIN users u ON u.id=best.user_id LEFT JOIN map_details d ON d.map_id=best.map_id
    WHERE best.map_id=?5 AND best.score_id=?6`, rule.id, userId, rule.generation, cap, mapId, unhex(scoreId)).first();
}

async function validateRecording(compressed, score, policy) {
  if (compressed.length < 18 || compressed[0] !== 31 || compressed[1] !== 139 || compressed[2] !== 8)
    fail(400, "A gzip-compressed .stkr recording is required");
  let raw;
  try {
    raw = await readBounded(new Response(compressed).body.pipeThrough(new DecompressionStream("gzip")), policy.maxRawBytes);
  } catch (e) {
    if (e instanceof HttpError) throw e;
    fail(400, "Invalid compressed recording");
  }
  const view = new DataView(raw.buffer, raw.byteOffset, raw.byteLength);
  if (raw.length < 42 || (raw.length - 12) % 30 || view.getUint32(0, true) !== 0x53544b52 || view.getInt32(4, true) !== 5)
    fail(400, "Unsupported Sticks recording; upload .stkr version 5");
  const activation = view.getFloat32(8, true);
  if (!Number.isFinite(activation) || activation < 0.85 || activation > 1
      || Math.abs(activation - score.activation) > 0.000001)
    fail(400, "Recording activation threshold differs from the score");
  let previous = -Infinity, first = null;
  for (let offset = 12; offset < raw.length; offset += 30) {
    const time = view.getFloat64(offset, true);
    if (!Number.isFinite(time) || time < previous) fail(400, "Invalid replay timestamps");
    first ??= time;
    if (time - first > policy.maxDurationSeconds * 1000)
      fail(413, `Replays longer than ${Math.round(policy.maxDurationSeconds / 60)} minutes are not stored`);
    previous = time;
    for (let axis = 0; axis < 4; axis++)
      if (!Number.isFinite(view.getFloat32(offset + 8 + axis * 4, true))) fail(400, "Invalid replay stick data");
    for (let button = 0; button < 6; button++)
      if (raw[offset + 24 + button] > 1) fail(400, "Invalid replay button data");
  }
  return raw.length;
}

// Portable, self-contained download, independent of a future profile or server copy:
// ASCII ZKR1 | uint32 LE JSON byte length | UTF-8 metadata | gzip(.stkr v5).
async function packageRecording(rule, score, compressed, rawBytes) {
  const metadata = new TextEncoder().encode(JSON.stringify({
    formatVersion: 1, ruleset: rule.id, player: { userId: score.user_id, username: score.username },
    score: scoreSummary(rule, score, null),
    recording: { format: "stkr", version: 5, encoding: "gzip", compressedBytes: compressed.length,
      rawBytes, sha256: hex(await crypto.subtle.digest("SHA-256", compressed)) },
  }));
  const result = new Uint8Array(8 + metadata.length + compressed.length);
  result.set([90, 75, 82, 49]);
  new DataView(result.buffer).setUint32(4, metadata.length, true);
  result.set(metadata, 8);
  result.set(compressed, 8 + metadata.length);
  return result;
}

const existingReplay = (env, rule, userId, mapId, scoreId) => stmt(env, `SELECT * FROM replays
  WHERE ruleset=? AND user_id=? AND generation=? AND map_id=? AND score_id=?`,
  rule.id, userId, rule.generation, mapId, unhex(scoreId)).first();

export async function uploadReplay(request, env, rule, userId, mapId, scoreId) {
  if (!env.REPLAYS) fail(503, "Replay storage is not configured");
  if (rule.id !== "sticks") fail(400, "This ruleset has no replay codec");
  if (await authenticate(request, env) !== userId) fail(403, "You can only upload your own replays");
  const policy = replayPolicy(env);
  const score = await selectedScore(env, rule, userId, mapId, scoreId, policy.retainedPerPlayer);
  if (!score) fail(409, "Score is not a current eligible top play; refresh the profile");
  const existing = await existingReplay(env, rule, userId, mapId, scoreId);
  if (existing?.state === "ready") {
    request.body?.cancel().catch(() => {});
    return json({ stored: true, alreadyStored: true, bytes: existing.bytes, sha256: existing.sha256 });
  }
  if (existing) throw new HttpError(409, "Replay upload or cleanup is in progress", 60);
  await limit(env, `replay-hour:${userId}`, policy.uploadsPerHour, 3600);
  await limit(env, `replay-day:${userId}`, policy.uploadsPerDay, 86400);
  if (request.headers.get("content-type")?.split(";")[0].trim() !== "application/gzip"
      || request.headers.has("content-encoding"))
    fail(415, "Send gzip bytes as application/gzip, without Content-Encoding");
  const length = request.headers.get("content-length");
  if (length !== null && (!/^\d+$/.test(length) || Number(length) > policy.maxCompressedBytes))
    fail(413, "Replay exceeds the compressed size limit");
  const compressed = await readBounded(request.body, policy.maxCompressedBytes);
  // Count malformed attempts too, before decompression or an R2 operation.
  await limit(env, `replay-bytes:${userId}`, policy.uploadBytesPerDay, 86400, compressed.length || 1);
  await limit(env, "replay-global-day", policy.globalUploadsPerDay, 86400);
  const rawBytes = await validateRecording(compressed, score, policy);
  const data = await packageRecording(rule, score, compressed, rawBytes);
  const hash = hex(await crypto.subtle.digest("SHA-256", data));
  const key = `${rule.id}/${userId}/${rule.generation}/${mapId}/${scoreId}/${random()}.zkr`;
  const time = now();
  // D1 serialises reservations. Concurrent users cannot overbook the global byte cap.
  // Eligibility is rechecked in the same statement as the reservation.
  const reserved = await stmt(env, `INSERT INTO replays
    (object_key,ruleset,user_id,generation,map_id,score_id,state,bytes,sha256,created_at,lease_until)
    SELECT ?5,?1,?2,?3,?6,?7,'pending',?8,?9,?10,?11
    WHERE (SELECT bytes FROM replay_storage WHERE id=1)+?8<=?12
      AND EXISTS(SELECT 1 FROM (${topScoresSql}) best WHERE best.map_id=?6 AND best.score_id=?7
        AND best.md5=?13 AND best.played_at=?14 AND best.difficulty_version=?15 AND best.pp=?16
        AND best.accuracy=?17 AND best.combo=?18 AND best.total_score=?19 AND best.activation=?20
        AND best.judgements=?21 AND best.mods=?22 AND best.rate=?23)
    ON CONFLICT DO NOTHING RETURNING object_key`,
    rule.id, userId, rule.generation, policy.retainedPerPlayer,
    key, mapId, unhex(scoreId), data.length, hash, time, time + 600, policy.maxStoredBytes,
    Uint8Array.from(score.md5), score.played_at, score.difficulty_version, score.pp, score.accuracy,
    score.combo, score.total_score, score.activation, Uint8Array.from(score.judgements), score.mods, score.rate).first();
  if (!reserved) {
    if ((await stmt(env, "SELECT bytes FROM replay_storage WHERE id=1").first()).bytes + data.length > policy.maxStoredBytes)
      fail(507, "Replay storage budget is full; score and local replay are unaffected");
    throw new HttpError(409, "Score or replay changed during upload; refresh and retry", 60);
  }
  try {
    await env.REPLAYS.put(key, data, { httpMetadata: { contentType: "application/vnd.zankai.replay" } });
    const published = await stmt(env, `UPDATE replays SET state='ready',lease_until=0
      WHERE object_key=?5 AND state='pending' AND lease_until>?6
      AND EXISTS(SELECT 1 FROM (${topScoresSql}) best WHERE best.map_id=?7 AND best.score_id=?8)
      RETURNING object_key`, rule.id, userId, rule.generation, policy.retainedPerPlayer,
      key, now(), mapId, unhex(scoreId)).first();
    if (!published) fail(409, "Score left the retained top plays during upload");
    return json({ stored: true, alreadyStored: false, bytes: data.length, sha256: hash,
      downloadPath: replayPath(rule, userId, mapId, scoreId) }, 201);
  } catch (error) {
    // Keep a durable tombstone until R2 deletion succeeds. Never release reserved
    // storage merely because a PUT failed (it may have committed before timing out).
    await stmt(env, "UPDATE replays SET state='deleting',lease_until=0 WHERE object_key=?", key).run();
    try { await deleteGarbage(env); } catch { /* Cron retries; no exception details exposed. */ }
    throw error;
  }
}

export async function downloadReplay(request, env, rule, userId, mapId, scoreId) {
  if (!env.REPLAYS) fail(503, "Replay storage is not configured");
  const policy = replayPolicy(env);
  if (!await selectedScore(env, rule, userId, mapId, scoreId, policy.retainedPerPlayer))
    fail(404, "Replay is no longer retained");
  const replay = await existingReplay(env, rule, userId, mapId, scoreId);
  if (replay?.state !== "ready") fail(404, "Replay is not available");
  const object = await env.REPLAYS.get(replay.object_key);
  if (!object) {
    // Let the owner upload again if the file was removed outside the service.
    await stmt(env, "DELETE FROM replays WHERE object_key=? AND state='ready'", replay.object_key).run();
    fail(404, "Replay file is unavailable");
  }
  return new Response(object.body, { headers: {
    "Content-Type": "application/vnd.zankai.replay", "Content-Length": String(object.size),
    "Content-Disposition": `attachment; filename="${rule.id}-${userId}-${mapId}-${scoreId}.zkr"`,
    "X-Replay-SHA256": replay.sha256, "X-Content-Type-Options": "nosniff",
    // No publicly cached URL may bypass a subsequent ban, map removal or retention change.
    "Cache-Control": "private, no-store",
  } });
}

export async function prunePlayer(env, rule, userId) {
  const policy = replayPolicy(env);
  const queued = await stmt(env, "SELECT revision FROM replay_cleanup_users WHERE ruleset=? AND user_id=?", rule.id, userId).first();
  await env.DB.batch([
    stmt(env, `UPDATE replays SET state='deleting'
      WHERE ruleset=?1 AND user_id=?2 AND state!='deleting'
      AND (generation!=?3 OR NOT EXISTS(
        SELECT 1 FROM (${topScoresSql}) best WHERE best.map_id=replays.map_id AND best.score_id=replays.score_id))`,
      rule.id, userId, rule.generation, policy.retainedPerPlayer),
    stmt(env, "DELETE FROM replay_cleanup_users WHERE ruleset=? AND user_id=? AND revision=?",
      rule.id, userId, queued?.revision ?? -1),
  ]);
}

export async function deleteGarbage(env) {
  if (!env.REPLAYS) return;
  const rows = (await stmt(env, "SELECT object_key FROM replays WHERE state='deleting' AND lease_until<=? LIMIT 1000", now()).all()).results;
  if (!rows.length) return;
  const keys = rows.map(r => r.object_key);
  await env.REPLAYS.delete(keys);
  await stmt(env, "DELETE FROM replays WHERE state='deleting' AND object_key IN (SELECT value FROM json_each(?))", JSON.stringify(keys)).run();
}

export async function maintainReplays(env) {
  if (!env.REPLAYS) return;
  const policy = replayPolicy(env);
  for (const id of Object.keys(env.RULESETS ?? {})) {
    const rule = resolveRuleset(env, id);
    await env.DB.batch([
      stmt(env, `INSERT INTO replay_cleanup_users(ruleset,user_id)
        SELECT DISTINCT ruleset,user_id FROM replays WHERE ruleset=?1
        AND NOT EXISTS(SELECT 1 FROM replay_settings WHERE ruleset=?1 AND generation=?2 AND retained=?3)
        ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1`,
        id, rule.generation, policy.retainedPerPlayer),
      stmt(env, `INSERT INTO replay_settings VALUES(?,?,?) ON CONFLICT(ruleset)
        DO UPDATE SET generation=excluded.generation,retained=excluded.retained
        WHERE generation!=excluded.generation OR retained!=excluded.retained`, id, rule.generation, policy.retainedPerPlayer),
    ]);
  }
  await stmt(env, `INSERT INTO replay_cleanup_users(ruleset,user_id)
    SELECT DISTINCT ruleset,user_id FROM replays WHERE ruleset NOT IN (SELECT key FROM json_each(?))
    ON CONFLICT(ruleset,user_id) DO UPDATE SET revision=revision+1`, JSON.stringify(env.RULESETS ?? {})).run();
  // Ten owners per run keeps a run below Free's 50 D1 queries for the current ruleset.
  const owners = (await stmt(env, "SELECT ruleset,user_id FROM replay_cleanup_users ORDER BY ruleset,user_id LIMIT 10").all()).results;
  for (const owner of owners) {
    if (Object.hasOwn(env.RULESETS ?? {}, owner.ruleset))
      await prunePlayer(env, resolveRuleset(env, owner.ruleset), owner.user_id);
    else await env.DB.batch([
      stmt(env, "UPDATE replays SET state='deleting' WHERE ruleset=? AND user_id=?", owner.ruleset, owner.user_id),
      stmt(env, "DELETE FROM replay_cleanup_users WHERE ruleset=? AND user_id=?", owner.ruleset, owner.user_id),
    ]);
  }
  await stmt(env, "UPDATE replays SET state='deleting' WHERE state='pending' AND lease_until<?", now()).run();
  await deleteGarbage(env);
  await sweepOrphans(env);
}

// Normally every object has a reservation before PUT. This bounded reconciliation
// also catches a PUT that finishes unusually late, after its expired lease was
// already collected, or a database restore that removed its reservation.
async function sweepOrphans(env) {
  const cursor = (await stmt(env, "SELECT cursor FROM replay_sweep WHERE id=1").first()).cursor;
  const page = await env.REPLAYS.list({ limit: 1000, ...(cursor ? { cursor } : {}) });
  const candidates = page.objects.filter(object =>
    /^[a-z][a-z0-9-]{0,31}\/\d+\/\d+\/\d+\/[a-f0-9]{32}\/[a-f0-9]{64}\.zkr$/.test(object.key)
    && object.uploaded.getTime() < Date.now() - 86400000).map(object => object.key);
  if (candidates.length) {
    const known = new Set((await stmt(env,
      "SELECT object_key FROM replays WHERE object_key IN (SELECT value FROM json_each(?))",
      JSON.stringify(candidates)).all()).results.map(r => r.object_key));
    const orphaned = candidates.filter(key => !known.has(key));
    if (orphaned.length) await env.REPLAYS.delete(orphaned);
  }
  await stmt(env, "UPDATE replay_sweep SET cursor=? WHERE id=1", page.truncated ? page.cursor : null).run();
}
