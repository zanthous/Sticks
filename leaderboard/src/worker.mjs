import { HttpError, digest, random, integer, resolveRuleset, validateScore, eligible, unhex, scoreValues, upsertScoreSql, updateTotalSql } from "./policy.mjs";
import { osuRequest, cachedLookup } from "./osu-api.mjs";
import { authenticate, edgeLimit, fail, json, limit, now, requireNotBanned, stmt } from "./http.mjs";
import { getTopPlays } from "./profiles.mjs";
import { deleteGarbage, downloadReplay, maintainReplays, prunePlayer, replayPolicy, uploadReplay } from "./replays.mjs";

async function body(request) {
  if (!request.headers.get("content-type")?.startsWith("application/json")) fail(415, "JSON required");
  const reader = request.body?.getReader();
  if (!reader) fail(400, "Body required");
  let bytes = 0, chunks = [];
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    bytes += value.length;
    if (bytes > 4096) { await reader.cancel(); fail(413, "Summary too large"); }
    chunks.push(value);
  }
  const joined = new Uint8Array(bytes);
  let offset = 0;
  for (const chunk of chunks) { joined.set(chunk, offset); offset += chunk.length; }
  try { return JSON.parse(new TextDecoder().decode(joined)); } catch { fail(400, "Invalid JSON"); }
}

async function publicToken(env) {
  return cachedLookup(env, "public-token", async () =>
    (await stmt(env, "SELECT token FROM service_tokens WHERE key='osu' AND expires>?", now() + 60).first())?.token,
  async () => {
    const token = await osuRequest(env, "/oauth/token", {
      method: "POST", headers: { "Content-Type": "application/json", "Accept": "application/json" },
      body: JSON.stringify({ client_id: Number(env.OSU_CLIENT_ID), client_secret: env.OSU_CLIENT_SECRET, grant_type: "client_credentials", scope: "public" }),
    });
    if (typeof token.access_token !== "string" || !integer(token.expires_in, 61, 31536000)) fail(503, "Invalid osu! token response");
    await stmt(env, "INSERT INTO service_tokens VALUES('osu',?,?) ON CONFLICT(key) DO UPDATE SET token=excluded.token,expires=excluded.expires", token.access_token, now() + token.expires_in).run();
    return token.access_token;
  });
}

async function getMap(env, id) {
  return cachedLookup(env, `map:${id}`, () =>
    stmt(env, "SELECT * FROM maps WHERE id=? AND checked_at>?", id, now() - 3600).first(),
  async () => {
    const token = await publicToken(env);
    const map = await osuRequest(env, `/api/v2/beatmaps/${id}`, { headers: { Authorization: `Bearer ${token}`, Accept: "application/json" } });
    if (map.id !== id || !/^[a-f0-9]{32}$/.test(map.checksum) || !integer(map.beatmapset_id, 1, 2147483647)) fail(503, "Invalid map metadata");
    const result = { id, set_id: map.beatmapset_id, md5: unhex(map.checksum), mode: map.mode_int, ranked: map.ranked, checked_at: now() };
    const text = value => typeof value === "string" ? value.slice(0, 256) : "";
    await env.DB.batch([
      stmt(env, `INSERT INTO maps VALUES(?,?,?,?,?,?) ON CONFLICT(id) DO UPDATE SET set_id=excluded.set_id,
        md5=excluded.md5,mode=excluded.mode,ranked=excluded.ranked,checked_at=excluded.checked_at`,
        id, result.set_id, result.md5, result.mode, result.ranked, result.checked_at),
      stmt(env, `INSERT INTO map_details VALUES(?,?,?,?) ON CONFLICT(map_id)
        DO UPDATE SET title=excluded.title,artist=excluded.artist,difficulty=excluded.difficulty`,
        id, text(map.beatmapset?.title), text(map.beatmapset?.artist), text(map.version)),
    ]);
    return result;
  });
}

async function submit(request, env, rule) {
  const userId = await authenticate(request, env);
  await limit(env, `submit:${userId}`, 220, 3600);
  const score = validateScore(await body(request), userId, rule);
  const map = await getMap(env, score.beatmapId);
  const approved = await stmt(env, "SELECT set_id FROM whitelisted_sets WHERE ruleset=? AND set_id=?", rule.id, score.beatmapSetId).first();
  if (!eligible(score, map, !!approved)) return json({ ruleset: rule.id, accepted: false });
  // Atomic upsert + aggregate avoids lost updates when two devices submit concurrently.
  await env.DB.batch([
    stmt(env, upsertScoreSql, ...scoreValues(score, rule.id)),
    stmt(env, updateTotalSql, userId, score.generation, rule.id),
  ]);
  // Scores still succeed if storage cleanup is temporarily unavailable. The score
  // triggers leave a durable queue entry, and replay reads always recheck eligibility.
  if (env.REPLAYS) {
    try { await prunePlayer(env, rule, userId); await deleteGarbage(env); }
    catch { /* Scheduled maintenance retries. Never roll back a saved PB for R2. */ }
  }
  return json({ ruleset: rule.id, accepted: true });
}

async function startLogin(request, env) {
  const s = await body(request);
  if (!integer(s?.userId, 1, 2147483647) || !/^[a-f0-9]{64}$/.test(s?.challenge)) fail(400, "Invalid login request");
  const address = request.headers.get("CF-Connecting-IP") || "local";
  await limit(env, `login:${await digest(address)}`, 10, 3600);
  await requireNotBanned(env, s.userId);
  const id = random();
  await stmt(env, "INSERT INTO logins(id,user_id,challenge,expires) VALUES(?,?,?,?)", id, s.userId, s.challenge, now() + 300).run();
  return json({ id, url: `${env.PUBLIC_ORIGIN}/auth/browser?id=${id}` });
}

async function browserLogin(url, env) {
  const id = url.searchParams.get("id");
  const state = random(), cookie = random();
  // browser_hash is never cleared, so a login opens once; the callback clearing state must not reopen it.
  const login = await stmt(env, "UPDATE logins SET state=?,browser_hash=? WHERE id=? AND expires>? AND browser_hash IS NULL RETURNING id", state, await digest(cookie), id, now()).first();
  if (!login) fail(400, "Login expired or already opened; reconnect in the game");
  const target = new URL("https://osu.ppy.sh/oauth/authorize");
  for (const [key, value] of Object.entries({ client_id: env.OSU_CLIENT_ID, redirect_uri: `${env.PUBLIC_ORIGIN}/auth/callback`, response_type: "code", scope: "identify public", state })) target.searchParams.set(key, value);
  return new Response(null, { status: 302, headers: {
    Location: target.href, "Cache-Control": "no-store", "Referrer-Policy": "no-referrer",
    "Set-Cookie": `zankai_login=${cookie}; HttpOnly; Secure; SameSite=Lax; Max-Age=300; Path=/auth/callback`,
  } });
}

async function callback(request, url, env) {
  const state = url.searchParams.get("state"), code = url.searchParams.get("code");
  const cookie = request.headers.get("Cookie")?.match(/(?:^|;\s*)zankai_login=([a-f0-9]{64})(?:;|$)/)?.[1];
  if (!cookie || !code || !/^[a-f0-9]{64}$/.test(state)) fail(400, "Sign-in cancelled or invalid");
  // Consume the OAuth state before exchanging the code. A failed exchange requires a new login.
  const login = await stmt(env, "UPDATE logins SET state=NULL WHERE state=? AND browser_hash=? AND expires>? RETURNING id,user_id", state, await digest(cookie), now()).first();
  if (!login) fail(400, "Sign-in expired");
  const tokens = await osuRequest(env, "/oauth/token", {
    method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" },
    body: JSON.stringify({ client_id: Number(env.OSU_CLIENT_ID), client_secret: env.OSU_CLIENT_SECRET, code, grant_type: "authorization_code", redirect_uri: `${env.PUBLIC_ORIGIN}/auth/callback` }),
  });
  if (typeof tokens?.access_token !== "string" || !tokens.access_token) fail(503, "osu! sign-in returned no access token; reconnect in the game");
  const user = await osuRequest(env, "/api/v2/me", { headers: { Authorization: `Bearer ${tokens.access_token}`, Accept: "application/json" } });
  if (user.id !== login.user_id) fail(403, "Sign in to the same osu! account you use in the game");
  // osu! includes is_restricted only for the signed-in account's own /me data. New accounts are welcome.
  if (user.is_restricted === true) fail(403, "Restricted osu! accounts can't share Sticks scores");
  await requireNotBanned(env, user.id);
  if (typeof user.username !== "string" || user.username.length > 64) fail(503, "Invalid user metadata");
  // Do not retain the player's osu! access or refresh tokens.
  await env.DB.batch([
    stmt(env, "INSERT INTO users VALUES(?,?) ON CONFLICT(id) DO UPDATE SET username=excluded.username", user.id, user.username),
    stmt(env, "UPDATE logins SET verified=1 WHERE id=?", login.id),
  ]);
  return new Response("Connected to Zankai osu!. You can close this tab and return to the game.", { headers: {
    "Content-Type": "text/plain; charset=utf-8", "Cache-Control": "no-store", "Referrer-Policy": "no-referrer",
    "Set-Cookie": "zankai_login=; HttpOnly; Secure; SameSite=Lax; Max-Age=0; Path=/auth/callback",
  } });
}

async function completeLogin(request, env) {
  const s = await body(request);
  if (!/^[a-f0-9]{64}$/.test(s?.id) || !/^[a-f0-9]{64}$/.test(s?.verifier)) fail(400, "Invalid login proof");
  const hash = await digest(s.verifier);
  const login = await stmt(env, "SELECT verified,user_id FROM logins WHERE id=? AND challenge=? AND expires>?", s.id, hash, now()).first();
  if (!login) fail(400, "Login expired");
  await requireNotBanned(env, login.user_id);
  if (!login.verified) return json({ pending: true });
  const token = random(), expires = now() + 30 * 86400;
  const results = await env.DB.batch([
    stmt(env, `INSERT INTO sessions(token_hash,user_id,expires)
      SELECT ?,user_id,? FROM logins WHERE id=? AND challenge=? AND verified=1 AND expires>?
      AND NOT EXISTS(SELECT 1 FROM banned_users WHERE banned_users.user_id=logins.user_id)
      RETURNING user_id`, await digest(token), expires, s.id, hash, now()),
    stmt(env, "DELETE FROM logins WHERE id=? AND challenge=? AND verified=1", s.id, hash),
  ]);
  const user = results[0].results[0];
  if (!user) fail(400, "Login already consumed");
  return json({ pending: false, token, userId: user.user_id, expires });
}

async function route(request, env) {
  const url = new URL(request.url), path = url.pathname;
  if (!env.PUBLIC_ORIGIN || new URL(env.PUBLIC_ORIGIN).origin !== url.origin) fail(503, "Leaderboard not configured");
  await edgeLimit(request, env, "API_RATE_LIMITER");
  const top = path.match(/^\/v1\/rulesets\/([a-z][a-z0-9-]{0,31})\/users\/([1-9]\d{0,9})\/top$/);
  if (top && request.method === "GET") {
    const rule = resolveRuleset(env, top[1]), userId = Number(top[2]);
    if (!integer(userId, 1, 2147483647)) fail(400, "Invalid player");
    await edgeLimit(request, env, "READ_RATE_LIMITER");
    return getTopPlays(url, env, rule, userId, replayPolicy(env));
  }
  const replay = path.match(/^\/v1\/rulesets\/([a-z][a-z0-9-]{0,31})\/users\/([1-9]\d{0,9})\/scores\/([1-9]\d{0,9})\/([a-f0-9]{32})\/replay$/);
  if (replay) {
    const rule = resolveRuleset(env, replay[1]), userId = Number(replay[2]), mapId = Number(replay[3]);
    if (url.search || !integer(userId, 1, 2147483647) || !integer(mapId, 1, 2147483647))
      fail(400, "Invalid replay request");
    if (request.method === "PUT") return uploadReplay(request, env, rule, userId, mapId, replay[4]);
    if (request.method === "GET") {
      await edgeLimit(request, env, "DOWNLOAD_RATE_LIMITER");
      return downloadReplay(request, env, rule, userId, mapId, replay[4]);
    }
  }
  const scoped = path.match(/^\/v1\/rulesets\/([a-z][a-z0-9-]{0,31})\/(policy|leaderboard|scores)$/);
  if (scoped) {
    const rule = resolveRuleset(env, scoped[1]), resource = scoped[2];
    if (resource !== "leaderboard" && url.search) fail(400, "Unexpected query parameters");
    if (request.method === "GET" && resource === "policy") {
      const sets = await stmt(env, "SELECT set_id FROM whitelisted_sets WHERE ruleset=? ORDER BY set_id", rule.id).all();
      return json({ ruleset: rule.id, generation: rule.generation, difficultyVersion: rule.difficultyVersion,
        whitelistedSets: sets.results.map(row => row.set_id), replays: { enabled: !!env.REPLAYS && rule.id === "sticks", ...replayPolicy(env) } });
    }
    if (request.method === "GET" && resource === "leaderboard") {
      await edgeLimit(request, env, "READ_RATE_LIMITER");
      const offset = Number(url.searchParams.get("offset") || 0);
      if ([...url.searchParams.keys()].some(key => key !== "offset") || url.searchParams.getAll("offset").length > 1
          || !integer(offset, 0, 10000)) fail(400, "Invalid page");
      const result = await stmt(env, `SELECT t.user_id AS userId,u.username,t.pp FROM totals t JOIN users u ON u.id=t.user_id
        WHERE t.ruleset=? AND t.generation=? AND t.pp>0 AND NOT EXISTS(SELECT 1 FROM banned_users b WHERE b.user_id=t.user_id)
        ORDER BY t.pp DESC,t.user_id LIMIT 50 OFFSET ?`, rule.id, rule.generation, offset).all();
      return json({ ruleset: rule.id, entries: result.results.map((r, i) => ({ rank: offset + i + 1, ...r })) });
    }
    if (request.method === "POST" && resource === "scores") return submit(request, env, rule);
  }
  if (request.method === "POST" && path === "/auth/start") return startLogin(request, env);
  if (request.method === "GET" && path === "/auth/browser") return browserLogin(url, env);
  if (request.method === "GET" && path === "/auth/callback") return callback(request, url, env);
  if (request.method === "POST" && path === "/auth/complete") return completeLogin(request, env);
  if (request.method === "POST" && path === "/v1/disconnect") {
    await authenticate(request, env);
    await stmt(env, "DELETE FROM sessions WHERE token_hash=?", await digest(request.headers.get("authorization").slice(7))).run();
    return json({ disconnected: true });
  }
  fail(404, "Not found");
}

export default {
  async fetch(request, env) {
    try { return await route(request, env); }
    catch (e) {
      // Never send exception dumps, SQL, OAuth codes or tokens to the caller/logs.
      return json({ error: e instanceof HttpError ? e.message : "Leaderboard temporarily unavailable" }, e instanceof HttpError ? e.status : 503,
        e instanceof HttpError && e.retryAfter ? { "Retry-After": String(e.retryAfter) } : {});
    }
  },
  async scheduled(event, env) {
    await env.DB.batch([
      stmt(env, "DELETE FROM logins WHERE expires<?", now()),
      stmt(env, "DELETE FROM sessions WHERE expires<?", now()),
      stmt(env, "DELETE FROM limits WHERE expires<?", now()),
      stmt(env, "DELETE FROM osu_lookup_locks WHERE retry_at<?", Date.now()),
    ]);
    await maintainReplays(env);
  },
};
