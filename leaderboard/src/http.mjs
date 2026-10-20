import { HttpError, digest } from "./policy.mjs";

export const now = () => Math.floor(Date.now() / 1000);
export const stmt = (env, sql, ...values) => env.DB.prepare(sql).bind(...values);
export const fail = (status, message) => { throw new HttpError(status, message); };
export const json = (value, status = 200, headers = {}) => Response.json(value, {
  status, headers: { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff", ...headers },
});

// Global, atomic fixed windows. Rejections do not keep incrementing the counter.
export async function limit(env, key, max, seconds = 60, amount = 1) {
  const time = now();
  const row = await stmt(env, `INSERT INTO limits(key,count,expires)
    SELECT ?,?,? WHERE ?<=?
    ON CONFLICT(key) DO UPDATE SET
      count=CASE WHEN expires<=? THEN excluded.count ELSE count+excluded.count END,
      expires=CASE WHEN expires<=? THEN excluded.expires ELSE expires END
    WHERE CASE WHEN expires<=? THEN excluded.count ELSE count+excluded.count END<=?
    RETURNING count`, key, amount, time + seconds, amount, max, time, time, time, max).first();
  if (!row) {
    const window = await stmt(env, "SELECT expires FROM limits WHERE key=?", key).first();
    throw new HttpError(429, "Try again later", Math.max(1, (window?.expires ?? time + seconds) - time));
  }
}

// Cloudflare's limiter runs before D1/R2. It is an approximate per-location shield;
// the authenticated upload/day/byte limits above are exact and shared globally.
export async function edgeLimit(request, env, binding) {
  if (!env[binding]) fail(503, "Rate limiter not configured");
  const key = await digest(request.headers.get("CF-Connecting-IP") || "unknown");
  if (!(await env[binding].limit({ key })).success)
    throw new HttpError(429, "Too many requests; retry later", 60);
}

export async function authenticate(request, env) {
  const token = request.headers.get("authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
  if (!token) fail(401, "Sign in required");
  const session = await stmt(env, "SELECT user_id FROM sessions WHERE token_hash=? AND expires>?", await digest(token), now()).first();
  if (!session) fail(401, "Session expired");
  await requireNotBanned(env, session.user_id);
  return session.user_id;
}

export async function requireNotBanned(env, userId) {
  if (await stmt(env, "SELECT user_id FROM banned_users WHERE user_id=?", userId).first())
    fail(403, "This account is banned from Zankai osu! leaderboards");
}

export async function readBounded(stream, maximum, timeoutMs = 60000) {
  if (!stream) fail(400, "Empty body");
  const reader = stream.getReader();
  let timer, length = 0;
  const chunks = [];
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new HttpError(408, "Upload timed out")), timeoutMs);
  });
  try {
    for (;;) {
      const { done, value } = await Promise.race([reader.read(), timeout]);
      if (done) break;
      length += value.byteLength;
      if (length > maximum) fail(413, "Replay exceeds the size limit");
      chunks.push(value);
    }
    const bytes = new Uint8Array(length);
    let offset = 0;
    for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
    return bytes;
  } finally {
    clearTimeout(timer);
    // Cancellation is best-effort: a decompressor may still have a write in flight.
    reader.cancel().catch(() => {});
  }
}
