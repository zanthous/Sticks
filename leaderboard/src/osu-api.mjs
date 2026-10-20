import { HttpError, random } from "./policy.mjs";

const stmt = (env, sql, ...values) => env.DB.prepare(sql).bind(...values);
const spacing = 1500;
const lease_duration = 30000; // Longer than the fetch timeout, but recovers after a terminated Worker.
const lookup_cooldown = 60000;
// Also covers a Worker terminated after acquiring a token but before publishing map metadata.
const lookup_lease = 120000;
const secondsUntil = time => Math.max(1, Math.ceil((time - Date.now()) / 1000));
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
const oauth_errors = new Set([
  "invalid_client", "invalid_grant", "invalid_request", "invalid_scope", "unauthorized_client",
  "unsupported_grant_type", "access_denied", "invalid_token", "insufficient_scope", "server_error", "temporarily_unavailable",
]);

// Only known error identifiers may leave the upstream response. Descriptions and exception
// dumps can contain credentials, authorization codes or tokens. Bound the read as well.
async function errorCode(response) {
  const reader = response.body?.getReader();
  if (!reader) return null;
  try {
    if (!response.headers.get("Content-Type")?.toLowerCase().includes("application/json")) return null;
    const decoder = new TextDecoder();
    let text = "", bytes = 0;
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      bytes += value.length;
      if (bytes > 4096) return null;
      text += decoder.decode(value, { stream: true });
    }
    const code = JSON.parse(text + decoder.decode())?.error;
    return oauth_errors.has(code) ? code : null;
  } catch { return null; }
  finally { try { await reader.cancel(); } catch { /* Preserve the original HTTP failure. */ } }
}

function diagnostic(operation, reason, status = null, code = null, format = null) {
  // Fixed categories only: never log request URLs, headers, bodies, or raw error text.
  console.warn("osu_verification_failed", { operation, reason, status, code, format });
  return `osu! ${operation} failed (${reason}${status === null ? "" : ` ${status}`}${code ? `; ${code}` : ""}); retry later`;
}

// No fixed-window reset or accumulated burst allowance: dispatches are spaced at least 1.5s
// apart (at most 40 in any rolling minute). A lease also prevents overlapping upstream calls.
async function acquire(env, owner) {
  for (let attempt = 0; attempt < 2; attempt++) {
    const time = Date.now();
    const claimed = await stmt(env, `UPDATE osu_api_state SET lease_owner=?,lease_until=?,next_request_at=?
      WHERE id=1 AND lease_until<=? AND blocked_until<=? AND next_request_at<=? RETURNING id`,
      owner, time + lease_duration, time + spacing, time, time, time).first();
    if (claimed) return;
    const state = await stmt(env, "SELECT * FROM osu_api_state WHERE id=1").first();
    if (state.blocked_until > Date.now())
      throw new HttpError(503, "osu! verification is cooling down; retry later", secondsUntil(state.blocked_until));
    // Allow the token -> metadata and OAuth token -> identity sequences to finish normally.
    // Do not build an unbounded queue of requests while another Worker is fetching.
    const delay = state.next_request_at - Date.now();
    if (attempt === 0 && state.lease_until <= Date.now() && delay > 0 && delay <= spacing) {
      await wait(delay);
      continue;
    }
    throw new HttpError(429, "osu! verification is busy; retry later",
      secondsUntil(Math.max(state.next_request_at, state.lease_until)));
  }
}

export function retryAfterUntil(value, time = Date.now()) {
  if (!value) return time;
  const seconds = /^\d+$/.test(value.trim()) ? Number(value) : NaN;
  const until = Number.isFinite(seconds) ? time + seconds * 1000 : Date.parse(value);
  return Number.isSafeInteger(until) && until > time ? until : time;
}

async function backOff(env, owner, retryAfter, message = "osu! verification unavailable; retry later") {
  const time = Date.now();
  const state = await stmt(env, `UPDATE osu_api_state SET
    blocked_until=MAX(blocked_until,?,? + MIN(900000,60000 * (1 << MIN(failures,4)))),
    failures=MIN(failures+1,5) WHERE id=1 AND lease_owner=? RETURNING blocked_until`,
    retryAfterUntil(retryAfter, time), time, owner).first();
  return new HttpError(503, message, secondsUntil(state?.blocked_until ?? time + lookup_cooldown));
}

export async function osuRequest(env, path, options = {}) {
  const owner = random();
  const operation = path === "/oauth/token" ? "token exchange" : path === "/api/v2/me" ? "account lookup" : "beatmap lookup";
  let receivedResponse = false;
  await acquire(env, owner);
  try {
    const response = await fetch(`https://osu.ppy.sh${path}`, {
      // Workers rejects redirect:"error" before sending anything. Manual returns 3xx
      // to the failure path below, without forwarding credentials to another URL.
      ...options, redirect: "manual", signal: AbortSignal.timeout(10000),
    });
    receivedResponse = true;
    if (!response.ok) {
      // A missing map only cools down that lookup; it must not stop unrelated players.
      if (response.status === 404) { await response.body?.cancel(); throw new HttpError(422, "osu! resource not found"); }
      const type = response.headers.get("Content-Type")?.toLowerCase() ?? "";
      const format = type.includes("application/json") ? "json" : type.includes("text/html") ? "html" : "other";
      const code = await errorCode(response);
      const message = diagnostic(operation, "HTTP", response.status, code, format);
      // A rejected request, such as an invalid sign-in code, is not an osu! outage. Callers can send those
      // at will, so they must not trigger the shared cooldown that every player's logins and lookups wait on.
      if (response.status === 400) throw new HttpError(400, message);
      throw await backOff(env, owner, response.headers.get("Retry-After"), message);
    }
    const result = await response.json();
    await stmt(env, "UPDATE osu_api_state SET failures=0 WHERE id=1 AND lease_owner=?", owner).run();
    return result;
  } catch (e) {
    if (e instanceof HttpError) throw e;
    // Timeouts, network failures and malformed upstream JSON also need shared backoff.
    const reason = e?.name === "TimeoutError" || e?.name === "AbortError" ? "timeout"
      : receivedResponse && e instanceof SyntaxError ? "invalid JSON" : "request error";
    throw await backOff(env, owner, undefined, diagnostic(operation, reason));
  } finally {
    await stmt(env, `UPDATE osu_api_state SET lease_owner=NULL,lease_until=0,
      next_request_at=MAX(next_request_at,?) WHERE id=1 AND lease_owner=?`, Date.now() + spacing, owner).run();
  }
}

export async function cachedLookup(env, key, readCache, refresh) {
  const cached = await readCache();
  if (cached) return cached;
  const owner = random(), time = Date.now();
  const claimed = await stmt(env, `INSERT INTO osu_lookup_locks(key,owner,retry_at) VALUES(?,?,?)
    ON CONFLICT(key) DO UPDATE SET owner=excluded.owner,retry_at=excluded.retry_at
    WHERE retry_at<=? RETURNING owner`, key, owner, time + lookup_lease, time).first();
  if (!claimed) {
    // The owner may have just published the result between our cache read and lease attempt.
    const completed = await readCache();
    if (completed) return completed;
    const lock = await stmt(env, "SELECT retry_at FROM osu_lookup_locks WHERE key=?", key).first();
    throw new HttpError(429, "osu! lookup pending or cooling down; retry later", secondsUntil(lock?.retry_at ?? time + lookup_cooldown));
  }
  try {
    // Recheck after acquiring: an earlier refresh might have completed during contention.
    return await readCache() ?? await refresh();
  } finally {
    // Measured from completion, not acquisition: waiting for a token must not shorten the
    // cooldown following an actual map request. Owner checks cannot release another lease.
    await stmt(env, "UPDATE osu_lookup_locks SET retry_at=? WHERE key=? AND owner=?",
      Date.now() + lookup_cooldown, key, owner).run();
  }
}
