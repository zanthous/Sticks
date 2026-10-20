import test from "node:test";
import assert from "node:assert/strict";
import { webcrypto } from "node:crypto";
import { Database } from "./database.mjs";
import worker from "../src/worker.mjs";
import { osuRequest, retryAfterUntil } from "../src/osu-api.mjs";
import { digest, unhex, validateScore, eligible, updateTotalSql, upsertScoreSql, scoreValues } from "../src/policy.mjs";
globalThis.crypto ??= webcrypto;
const timestamp = () => Math.floor(Date.now() / 1000);

const token = "a".repeat(64), md5 = "0123456789abcdef0123456789abcdef";
function score(overrides = {}) {
  return { scoreId: "1".repeat(32), userId: 42, beatmapId: 1, beatmapSetId: 10, sourceMd5: md5,
    playedAt: Date.now() - 10000, generation: 1, difficultyVersion: 202609163, mods: 0, rate: 1,
    pp: 100, accuracy: 0.98, maxCombo: 500, totalScore: 980000, activationThreshold: 0.97,
    judgements: [100, 2, 0, 0, 100, 2, 0, 0, 0, 0, 0, 0, 100, 0, 2, 0], ...overrides };
}
async function setup() {
  const env = { DB: new Database(), PUBLIC_ORIGIN: "https://sticks.example", RULESETS: { sticks: { generation: 1, difficultyVersion: 202609163, judgementCount: 16 } }, OSU_CLIENT_ID: "1", OSU_CLIENT_SECRET: "test-only" };
  for (const name of ["API_RATE_LIMITER", "READ_RATE_LIMITER", "DOWNLOAD_RATE_LIMITER"])
    env[name] = { limit: async () => ({ success: true }) };
  await env.DB.prepare("INSERT INTO users VALUES(42,'Player')").bind().run();
  await env.DB.prepare("INSERT INTO sessions VALUES(?,?,?)").bind(await digest(token), 42, timestamp() + 3600).run();
  await addMap(env, 1);
  return env;
}
async function addMap(env, id, set = 10, ranked = 1, mode = 0) {
  await env.DB.prepare("INSERT INTO maps VALUES(?,?,?,?,?,?)").bind(id, set, unhex(md5), mode, ranked, timestamp()).run();
}
function call(env, path, body, auth = token) {
  return worker.fetch(new Request(env.PUBLIC_ORIGIN + path, {
    method: body === undefined ? "GET" : "POST",
    headers: { "Content-Type": "application/json", ...(auth ? { Authorization: `Bearer ${auth}` } : {}) },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  }), env);
}
const read = async (env, sql, ...args) => env.DB.prepare(sql).bind(...args).all();
const cachePublicToken = env => env.DB.prepare("INSERT INTO service_tokens VALUES('osu','public-token',?)").bind(timestamp() + 3600).run();
const mapMetadata = id => ({ id, beatmapset_id: 10, checksum: md5, mode_int: 0, ranked: 1 });
const scopedPath = (ruleset, resource) => `/v1/rulesets/${ruleset}/${resource}`;
function enableTestRuleset(env, policy = {}) {
  env.RULESETS["test-mode"] = { ...env.RULESETS.sticks, ...policy };
}

test("accepted PBs, weighted totals, duplicate and worse submissions", async () => {
  const env = await setup(); await addMap(env, 2);
  assert.equal((await call(env, "/v1/rulesets/sticks/scores", score())).status, 200);
  await call(env, "/v1/rulesets/sticks/scores", score());
  await call(env, "/v1/rulesets/sticks/scores", score({ pp: 10 }));
  await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2, pp: 80 }));
  const ranking = await (await call(env, "/v1/rulesets/sticks/leaderboard")).json();
  assert.equal(ranking.entries[0].pp, 176);
  assert.equal((await read(env, "SELECT * FROM scores")).results.length, 2);
  await call(env, "/v1/rulesets/sticks/scores", score({ pp: 120 }));
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 196);
});

test("Approved maps award pp like Ranked; Qualified and Loved do not", async () => {
  const env = await setup(); await addMap(env, 2, 10, 2); await addMap(env, 3, 10, 3); await addMap(env, 4, 10, 4);
  const submit = async beatmapId =>
    (await (await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId, scoreId: String(beatmapId).repeat(32) }))).json()).accepted;
  assert.equal(await submit(2), true, "Approved");
  assert.equal(await submit(3), false, "Qualified");
  assert.equal(await submit(4), false, "Loved");
  const totalPP = async () => (await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp;
  assert.equal(await totalPP(), 100);
  const top = await (await call(env, "/v1/rulesets/sticks/users/42/top")).json();
  assert.deepEqual(top.entries.map(e => e.beatmapId), [2]);
  // Whitelist changes rebuild every total through the calculated_totals view, which must keep counting Approved maps.
  await env.DB.prepare("INSERT INTO whitelisted_sets VALUES('sticks',99)").bind().run();
  assert.equal(await totalPP(), 100);
});

test("set whitelist includes all standard diffs but rejects edits and other modes", async () => {
  const env = await setup(); await addMap(env, 2, 20, 0); await addMap(env, 3, 20, 0); await addMap(env, 4, 20, 0, 3);
  assert.equal((await (await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2, beatmapSetId: 20 }))).json()).accepted, false);
  await env.DB.prepare("INSERT INTO whitelisted_sets VALUES('sticks',20)").bind().run();
  for (const beatmapId of [2, 3]) assert.equal((await (await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId, beatmapSetId: 20 }))).json()).accepted, true);
  for (const invalid of [{ beatmapId: 4, beatmapSetId: 20 }, { sourceMd5: "f".repeat(32) }, { beatmapSetId: 999 }])
    assert.equal((await (await call(env, "/v1/rulesets/sticks/scores", score(invalid))).json()).accepted, false);
  assert.deepEqual((await (await call(env, "/v1/rulesets/sticks/policy")).json()).whitelistedSets, [20]);
});

test("authentication, generation, unknown fields, impossible values and mods are rejected", async () => {
  const env = await setup();
  assert.equal((await call(env, "/v1/rulesets/sticks/scores", score(), "")).status, 401);
  for (const invalid of [{ userId: 99 }, { generation: 2 }, { difficultyVersion: 1 }, { pp: -1 }, { pp: null },
    { accuracy: 1.1 }, { mods: 256 }, { mods: 96 }, { mods: 6 }, { rate: 1.5 }, { replay: "never accept this" },
    { sourceMd5: "bad" }, { judgements: [1] }, { playedAt: Date.now() + 999999 }])
    assert.equal((await call(env, "/v1/rulesets/sticks/scores", score(invalid))).status, 400, JSON.stringify(invalid));
  assert.equal((await read(env, "SELECT * FROM scores")).results.length, 0);
});

test("No Mod only: Strum and every other mod bit are rejected", async () => {
  const env = await setup();
  await call(env, "/v1/rulesets/sticks/scores", score());
  for (let bit = 1; bit <= 128; bit *= 2)
    assert.equal((await call(env, "/v1/rulesets/sticks/scores", score({ mods: bit, pp: 150 }))).status, 400);
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 100);
});

test("only top 100 contribute and judgements occupy 64 bytes", async () => {
  const env = await setup();
  for (let i = 1; i <= 105; i++) {
    if (i > 1) await addMap(env, i);
    const s = validateScore(score({ beatmapId: i }), 42, env.RULESETS.sticks);
    await env.DB.prepare(upsertScoreSql).bind(...scoreValues(s, "sticks")).run();
  }
  await env.DB.prepare(updateTotalSql).bind(42, 1, "sticks").run();
  const { pp } = (await read(env, "SELECT pp FROM totals")).results[0];
  assert.ok(Math.abs(pp - 100 * (1 - 0.95 ** 100) / 0.05) < 1e-8);
  assert.equal((await read(env, "SELECT length(judgements) AS size FROM scores LIMIT 1")).results[0].size, 64);
});

test("body and pagination limits", async () => {
  const env = await setup();
  assert.equal((await call(env, "/v1/rulesets/sticks/scores", { ...score(), replay: "x".repeat(5000) })).status, 413);
  assert.equal((await call(env, "/v1/rulesets/sticks/leaderboard?offset=-1")).status, 400);
  assert.equal((await call(env, "/v1/rulesets/sticks/leaderboard?style=2")).status, 400);
});

test("login proof, single-use completion, expiry and session revocation", async () => {
  const env = await setup(), verifier = "b".repeat(64);
  const started = await (await call(env, "/auth/start", { userId: 42, challenge: await digest(verifier) })).json();
  assert.equal((await (await call(env, "/auth/complete", { id: started.id, verifier })).json()).pending, true);
  assert.equal((await call(env, "/auth/complete", { id: started.id, verifier: "c".repeat(64) })).status, 400);
  await env.DB.prepare("UPDATE logins SET verified=1 WHERE id=?").bind(started.id).run();
  const complete = await (await call(env, "/auth/complete", { id: started.id, verifier })).json();
  assert.equal(complete.userId, 42); assert.match(complete.token, /^[a-f0-9]{64}$/);
  assert.equal((await call(env, "/auth/complete", { id: started.id, verifier })).status, 400);
  assert.equal((await call(env, "/v1/rulesets/sticks/scores", score(), complete.token)).status, 200);
  await call(env, "/v1/disconnect", {}, complete.token);
  assert.equal((await call(env, "/v1/rulesets/sticks/scores", score(), complete.token)).status, 401);
});

test("OAuth binds state to browser cookie and verifies the expected account", async () => {
  const env = await setup(), verifier = "b".repeat(64);
  const started = await (await call(env, "/auth/start", { userId: 42, challenge: await digest(verifier) })).json();
  const redirect = await call(env, `/auth/browser?id=${started.id}`);
  assert.equal(redirect.status, 302);
  const oauth = new URL(redirect.headers.get("location"));
  assert.equal(oauth.origin, "https://osu.ppy.sh");
  assert.equal(oauth.searchParams.get("redirect_uri"), env.PUBLIC_ORIGIN + "/auth/callback");
  const path = `/auth/callback?state=${oauth.searchParams.get("state")}&code=sample`;
  assert.equal((await call(env, path)).status, 400);
  const originalFetch = globalThis.fetch;
  try {
    globalThis.fetch = async url => Response.json(String(url).includes("/oauth/token") ? { access_token: "temporary", expires_in: 3600 } : { id: 99, username: "Wrong account" });
    const response = await worker.fetch(new Request(env.PUBLIC_ORIGIN + path, { headers: { Cookie: redirect.headers.get("set-cookie").split(";")[0] } }), env);
    assert.equal(response.status, 403);
    assert.equal((await read(env, "SELECT verified FROM logins WHERE id=?", started.id)).results[0].verified, 0);
  } finally { globalThis.fetch = originalFetch; }
});

test("restricted osu! accounts cannot connect", async () => {
  const env = await setup(), verifier = "b".repeat(64);
  const started = await (await call(env, "/auth/start", { userId: 42, challenge: await digest(verifier) })).json();
  const redirect = await call(env, `/auth/browser?id=${started.id}`);
  const state = new URL(redirect.headers.get("location")).searchParams.get("state");
  const originalFetch = globalThis.fetch;
  try {
    globalThis.fetch = async url => Response.json(String(url).includes("/oauth/token")
      ? { access_token: "temporary", expires_in: 3600 } : { id: 42, username: "Player", is_restricted: true });
    const response = await worker.fetch(new Request(`${env.PUBLIC_ORIGIN}/auth/callback?state=${state}&code=sample`,
      { headers: { Cookie: redirect.headers.get("set-cookie").split(";")[0] } }), env);
    assert.equal(response.status, 403);
    assert.match(await response.text(), /Restricted/);
    assert.equal((await read(env, "SELECT verified FROM logins WHERE id=?", started.id)).results[0].verified, 0);
    assert.equal((await (await call(env, "/auth/complete", { id: started.id, verifier })).json()).pending, true, "No session is issued");
  } finally { globalThis.fetch = originalFetch; }
});

test("transaction rolls back on aggregate failure", async () => {
  const env = await setup();
  await assert.rejects(env.DB.batch([
    env.DB.prepare(upsertScoreSql).bind(...scoreValues(validateScore(score(), 42, env.RULESETS.sticks), "sticks")),
    env.DB.prepare("INSERT INTO nonexistent VALUES(1)").bind(),
  ]));
  assert.equal((await read(env, "SELECT * FROM scores")).results.length, 0);
});

test("whitelist removal and source checksum changes immediately update all stored totals", async () => {
  const env = await setup(); await addMap(env, 2, 20, 0);
  await env.DB.prepare("INSERT INTO whitelisted_sets VALUES('sticks',20)").bind().run();
  await call(env, "/v1/rulesets/sticks/scores", score());
  await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2, beatmapSetId: 20, pp: 80 }));
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 176);
  await env.DB.prepare("DELETE FROM whitelisted_sets WHERE ruleset='sticks' AND set_id=20").bind().run();
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 100);
  await env.DB.prepare("INSERT INTO whitelisted_sets VALUES('sticks',20)").bind().run();
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 176);
  await env.DB.prepare("UPDATE maps SET md5=? WHERE id=1").bind(unhex("f".repeat(32))).run();
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 80);
  await call(env, "/v1/rulesets/sticks/scores", score({ sourceMd5: "f".repeat(32), pp: 50 }));
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 80 + 50 * 0.95);
});

test("cached osu! metadata means submitting does not make external API calls", async () => {
  const env = await setup(), originalFetch = globalThis.fetch;
  try {
    globalThis.fetch = () => { throw new Error("Unexpected external request"); };
    assert.equal((await (await call(env, "/v1/rulesets/sticks/scores", score())).json()).accepted, true);
  } finally { globalThis.fetch = originalFetch; }
});

test("rate limit retains the previous PB and returns a retryable response", async () => {
  const env = await setup(); await call(env, "/v1/rulesets/sticks/scores", score());
  await env.DB.prepare("UPDATE limits SET count=220 WHERE key='submit:42'").bind().run();
  assert.equal((await call(env, "/v1/rulesets/sticks/scores", score({ pp: 200 }))).status, 429);
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 100);
});

test("successful OAuth keeps no player osu! token and completes only for the requesting client", async () => {
  const env = await setup(), verifier = "b".repeat(64), originalFetch = globalThis.fetch;
  const started = await (await call(env, "/auth/start", { userId: 42, challenge: await digest(verifier) })).json();
  const redirect = await call(env, `/auth/browser?id=${started.id}`);
  const state = new URL(redirect.headers.get("location")).searchParams.get("state");
  try {
    globalThis.fetch = async url => Response.json(String(url).includes("/oauth/token")
      ? { access_token: "must-not-be-retained", refresh_token: "also-secret", expires_in: 3600 }
      : { id: 42, username: "Renamed" });
    const response = await worker.fetch(new Request(`${env.PUBLIC_ORIGIN}/auth/callback?state=${state}&code=sample`, {
      headers: { Cookie: redirect.headers.get("set-cookie").split(";")[0] },
    }), env);
    assert.equal(response.status, 200);
    const completed = await (await call(env, "/auth/complete", { id: started.id, verifier })).json();
    assert.equal(completed.userId, 42);
    assert.notEqual(completed.token, "must-not-be-retained");
    assert.equal((await read(env, "SELECT * FROM service_tokens")).results.length, 0);
    assert.equal((await read(env, "SELECT username FROM users WHERE id=42")).results[0].username, "Renamed");
  } finally { globalThis.fetch = originalFetch; }
});

test("uncached map is checked against osu!, then reuses the cache", async () => {
  const env = await setup(), originalFetch = globalThis.fetch;
  let calls = 0;
  try {
    globalThis.fetch = async url => {
      calls++;
      return Response.json(String(url).includes("/oauth/token") ? { access_token: "public-token", expires_in: 3600 }
        : { id: 2, beatmapset_id: 10, checksum: md5, mode_int: 0, ranked: 1 });
    };
    const payload = score({ beatmapId: 2 });
    assert.equal((await (await call(env, "/v1/rulesets/sticks/scores", payload)).json()).accepted, true);
    assert.equal(calls, 2);
    assert.equal((await (await call(env, "/v1/rulesets/sticks/scores", payload)).json()).accepted, true);
    assert.equal(calls, 2);
  } finally { globalThis.fetch = originalFetch; }
});

test("manual ban blocks existing sessions and hides rankings without deleting scores; unban restores them", async () => {
  const env = await setup();
  await call(env, "/v1/rulesets/sticks/scores", score());
  await env.DB.prepare("INSERT INTO banned_users VALUES(42)").bind().run();
  assert.equal((await call(env, "/v1/rulesets/sticks/scores", score({ pp: 200 }))).status, 403);
  assert.deepEqual((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries, []);
  assert.equal((await read(env, "SELECT pp FROM scores WHERE user_id=42")).results[0].pp, 100);
  assert.equal((await call(env, "/auth/start", { userId: 42, challenge: "b".repeat(64) })).status, 403);
  await env.DB.prepare("DELETE FROM banned_users WHERE user_id=42").bind().run();
  assert.equal((await (await call(env, "/v1/rulesets/sticks/leaderboard")).json()).entries[0].pp, 100);
  assert.equal((await call(env, "/v1/rulesets/sticks/scores", score({ pp: 200 }))).status, 200);
});

test("accounts can be banned before joining or while sign-in is pending", async () => {
  const env = await setup(), verifier = "b".repeat(64);
  await env.DB.prepare("INSERT INTO banned_users VALUES(99)").bind().run();
  assert.equal((await call(env, "/auth/start", { userId: 99, challenge: await digest(verifier) })).status, 403);
  const started = await (await call(env, "/auth/start", { userId: 42, challenge: await digest(verifier) })).json();
  await env.DB.prepare("UPDATE logins SET verified=1 WHERE id=?").bind(started.id).run();
  await env.DB.prepare("INSERT INTO banned_users VALUES(42)").bind().run();
  assert.equal((await call(env, "/auth/complete", { id: started.id, verifier })).status, 403);
  assert.equal((await read(env, "SELECT count(*) AS n FROM sessions")).results[0].n, 1);
});

test("global spacing covers every rolling minute, including old fixed-window boundaries", async () => {
  const env = await setup(), originalFetch = globalThis.fetch, originalNow = Date.now, originalTimeout = globalThis.setTimeout;
  let time = originalNow();
  const dispatched = [];
  try {
    Date.now = () => time;
    // The caller never advances time: only the actual pacing waits may do that.
    globalThis.setTimeout = (callback, delay) => { time += delay; callback(); return 0; };
    globalThis.fetch = async () => { dispatched.push(time); return Response.json({}); };
    for (let i = 0; i < 100; i++) {
      await osuRequest(env, "/api/v2/beatmaps/1");
    }
    for (let i = 1; i < dispatched.length; i++) assert.ok(dispatched[i] - dispatched[i - 1] >= 1500);
    for (const end of dispatched)
      assert.ok(dispatched.filter(t => t > end - 60000 && t <= end).length <= 40);
    assert.equal(dispatched.length, 100);
  } finally { globalThis.fetch = originalFetch; Date.now = originalNow; globalThis.setTimeout = originalTimeout; }
});

test("separate Worker instances share one in-flight upstream lease", async () => {
  const { osuRequest: anotherWorker } = await import("../src/osu-api.mjs?another-worker");
  const env = await setup(), originalFetch = globalThis.fetch;
  let release, announce, calls = 0;
  const started = new Promise(resolve => { announce = resolve; });
  const pending = new Promise(resolve => { release = resolve; });
  try {
    globalThis.fetch = async () => { calls++; announce(); await pending; return Response.json({}); };
    const first = osuRequest(env, "/api/v2/beatmaps/1");
    await started;
    const others = await Promise.allSettled(Array.from({ length: 10 }, () => anotherWorker({ ...env }, "/api/v2/beatmaps/2")));
    assert.ok(others.every(r => r.status === "rejected" && r.reason.status === 429 && r.reason.retryAfter > 0));
    assert.equal(calls, 1);
    release(); await first;
    const state = (await read(env, "SELECT * FROM osu_api_state")).results[0];
    assert.equal(state.lease_owner, null);
    assert.equal(state.lease_until, 0);
  } finally { release(); globalThis.fetch = originalFetch; }
});

test("simultaneous submissions share a map refresh, then use its published cache", async () => {
  const env = await setup(), originalFetch = globalThis.fetch;
  await cachePublicToken(env);
  let release, announce, calls = 0;
  const started = new Promise(resolve => { announce = resolve; });
  const pending = new Promise(resolve => { release = resolve; });
  try {
    globalThis.fetch = async () => { calls++; announce(); await pending; return Response.json(mapMetadata(2)); };
    const first = call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2 }));
    await started;
    const second = await call({ ...env }, "/v1/rulesets/sticks/scores", score({ beatmapId: 2, pp: 200 }));
    assert.equal(second.status, 429);
    assert.ok(Number(second.headers.get("Retry-After")) > 0);
    assert.equal(calls, 1);
    release(); assert.equal((await first).status, 200);
    assert.equal((await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2, pp: 200 }))).status, 200);
    assert.equal(calls, 1);
    assert.equal((await read(env, "SELECT pp FROM scores WHERE map_id=2")).results[0].pp, 200);
  } finally { release(); globalThis.fetch = originalFetch; }
});

test("concurrent cache misses also deduplicate public-token refreshes", async () => {
  const env = await setup(), originalFetch = globalThis.fetch;
  let release, announce;
  const urls = [];
  const started = new Promise(resolve => { announce = resolve; });
  const pending = new Promise(resolve => { release = resolve; });
  try {
    globalThis.fetch = async url => {
      urls.push(url);
      if (url.endsWith("/oauth/token")) { announce(); await pending; return Response.json({ access_token: "public-token", expires_in: 3600 }); }
      return Response.json(mapMetadata(2));
    };
    const first = call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2 }));
    await started;
    assert.equal((await call({ ...env }, "/v1/rulesets/sticks/scores", score({ beatmapId: 3 }))).status, 429);
    assert.equal(urls.length, 1);
    release(); assert.equal((await first).status, 200);
    assert.equal(urls.filter(url => url.endsWith("/oauth/token")).length, 1);
    assert.equal(urls.length, 2);
  } finally { release(); globalThis.fetch = originalFetch; }
});

test("failed and missing map lookups cool down for a minute without accepting stale metadata", async () => {
  const env = await setup(), originalFetch = globalThis.fetch, originalNow = Date.now;
  await cachePublicToken(env);
  await addMap(env, 2);
  await env.DB.prepare("UPDATE maps SET checked_at=0 WHERE id=2").bind().run();
  let time = originalNow(), calls = 0;
  try {
    Date.now = () => time;
    globalThis.fetch = async () => { calls++; time += 10000; return new Response(null, { status: 404 }); };
    assert.equal((await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2 }))).status, 422);
    time += 59000;
    const blocked = await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2 }));
    assert.equal(blocked.status, 429);
    assert.equal(blocked.headers.get("Retry-After"), "1");
    assert.equal(calls, 1);
    assert.equal((await read(env, "SELECT * FROM scores")).results.length, 0);
    assert.equal((await read(env, "SELECT blocked_until FROM osu_api_state")).results[0].blocked_until, 0);
    time += 1000;
    assert.equal((await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2 }))).status, 422);
    assert.equal(calls, 2);
  } finally { globalThis.fetch = originalFetch; Date.now = originalNow; }
});

test("Retry-After accepts seconds and HTTP dates, ignoring invalid and past values", () => {
  const time = Date.parse("2026-10-19T12:00:00Z");
  assert.equal(retryAfterUntil("120", time), time + 120000);
  assert.equal(retryAfterUntil("Mon, 19 Oct 2026 12:05:00 GMT", time), time + 300000);
  for (const value of [null, "", "nonsense", "Mon, 19 Oct 2026 11:00:00 GMT", "9".repeat(30)])
    assert.equal(retryAfterUntil(value, time), time);
});

test("osu! rate limits pause all uncached requests but cached scores and leaderboard browsing still work", async () => {
  const env = await setup(), originalFetch = globalThis.fetch, originalNow = Date.now;
  await cachePublicToken(env);
  const time = originalNow();
  let calls = 0;
  try {
    Date.now = () => time;
    globalThis.fetch = async () => { calls++; return new Response(null, { status: 429, headers: { "Retry-After": "120" } }); };
    const failed = await call(env, "/v1/rulesets/sticks/scores", score({ beatmapId: 2 }));
    assert.equal(failed.status, 503);
    assert.equal(failed.headers.get("Retry-After"), "120");
    const other = await call({ ...env }, "/v1/rulesets/sticks/scores", score({ beatmapId: 3 }));
    assert.equal(other.status, 503);
    assert.equal(other.headers.get("Retry-After"), "120");
    assert.equal((await call(env, "/v1/rulesets/sticks/scores", score())).status, 200);
    assert.equal((await call(env, "/v1/rulesets/sticks/leaderboard")).status, 200);
    assert.equal((await call(env, "/v1/rulesets/sticks/policy")).status, 200);
    assert.equal(calls, 1);
    assert.equal((await read(env, "SELECT map_id FROM scores")).results[0].map_id, 1);
  } finally { globalThis.fetch = originalFetch; Date.now = originalNow; }
});

test("shared exponential backoff covers server errors and network failures, and resets after success", async () => {
  const env = await setup(), originalFetch = globalThis.fetch, originalNow = Date.now;
  let time = originalNow(), calls = 0, healthy = false;
  try {
    Date.now = () => time;
    globalThis.fetch = async () => {
      calls++;
      if (healthy) return Response.json({});
      if (calls % 2 === 0) throw new TypeError("Network unavailable");
      return new Response(null, { status: 503 });
    };
    for (const delay of [60, 120, 240, 480, 900, 900]) {
      await assert.rejects(osuRequest(env, "/api/v2/beatmaps/1"), e => e.status === 503 && e.retryAfter === delay);
      const before = calls;
      await assert.rejects(osuRequest({ ...env }, "/oauth/token"), e => e.status === 503 && e.retryAfter === delay);
      assert.equal(calls, before);
      time += delay * 1000;
    }
    healthy = true;
    await osuRequest(env, "/api/v2/beatmaps/1");
    assert.equal((await read(env, "SELECT failures FROM osu_api_state")).results[0].failures, 0);
    healthy = false; time += 1500;
    await assert.rejects(osuRequest(env, "/api/v2/beatmaps/1"), e => e.status === 503 && e.retryAfter === 60);
  } finally { globalThis.fetch = originalFetch; Date.now = originalNow; }
});

test("long upstream Retry-After dates are not shortened by our backoff cap", async () => {
  const env = await setup(), originalFetch = globalThis.fetch, originalNow = Date.now;
  const time = Date.parse("2026-10-19T12:00:00Z");
  try {
    Date.now = () => time;
    globalThis.fetch = async () => new Response(null, { status: 429, headers: { "Retry-After": "Mon, 19 Oct 2026 13:00:00 GMT" } });
    await assert.rejects(osuRequest(env, "/api/v2/me"), e => e.status === 503 && e.retryAfter === 3600);
    assert.equal((await read(env, "SELECT blocked_until FROM osu_api_state")).results[0].blocked_until, time + 3600000);
  } finally { globalThis.fetch = originalFetch; Date.now = originalNow; }
});

test("invalid upstream JSON also triggers shared cooldown", async () => {
  const env = await setup(), originalFetch = globalThis.fetch;
  try {
    globalThis.fetch = async () => new Response("not JSON");
    await assert.rejects(osuRequest(env, "/api/v2/beatmaps/1"), e => e.status === 503 && e.retryAfter > 0);
    assert.equal((await read(env, "SELECT failures FROM osu_api_state")).results[0].failures, 1);
  } finally { globalThis.fetch = originalFetch; }
});

test("OAuth errors expose the stage and known error code without leaking credentials", async t => {
  const env = await setup(), originalFetch = globalThis.fetch, logs = [];
  t.mock.method(console, "warn", (...args) => logs.push(args));
  const started = await (await call(env, "/auth/start", { userId: 42, challenge: "b".repeat(64) })).json();
  const redirect = await call(env, `/auth/browser?id=${started.id}`);
  const state = new URL(redirect.headers.get("location")).searchParams.get("state");
  const request = new Request(`${env.PUBLIC_ORIGIN}/auth/callback?state=${state}&code=private-login-code`, {
    headers: { Cookie: redirect.headers.get("set-cookie").split(";")[0] },
  });
  let calls = 0;
  try {
    globalThis.fetch = async (url, options) => {
      calls++;
      assert.equal(url, "https://osu.ppy.sh/oauth/token");
      assert.equal(JSON.parse(options.body).client_secret, "test-only");
      return Response.json({ error: "invalid_client", error_description: "test-only private-login-code private-token" }, { status: 401 });
    };
    const response = await worker.fetch(request, env);
    assert.equal(response.status, 503);
    assert.ok(Number(response.headers.get("Retry-After")) > 0);
    const body = await response.text();
    assert.match(body, /token exchange failed \(HTTP 401; invalid_client\)/);
    assert.deepEqual(logs, [["osu_verification_failed", {
      operation: "token exchange", reason: "HTTP", status: 401, code: "invalid_client", format: "json",
    }]]);
    for (const secret of ["test-only", "private-login-code", "private-token", state])
      assert.ok(!(body + JSON.stringify(logs)).includes(secret));
    assert.equal((await read(env, "SELECT verified FROM logins WHERE id=?", started.id)).results[0].verified, 0);
    assert.equal((await read(env, "SELECT failures,lease_until FROM osu_api_state")).results[0].failures, 1);
    assert.equal((await worker.fetch(request, env)).status, 400, "OAuth state remains single-use after failure");
    assert.equal((await call(env, `/auth/browser?id=${started.id}`)).status, 400, "A login cannot be reopened after its callback");
    await assert.rejects(osuRequest(env, "/oauth/token"), e => e.status === 503 && /cooling down/.test(e.message));
    assert.equal(calls, 1);
    assert.equal(logs.length, 1, "Cooldown does not repeatedly log an upstream failure");
  } finally { globalThis.fetch = originalFetch; }
});

test("a rejected sign-in code fails only that request and does not trigger the shared cooldown", async t => {
  const env = await setup(), originalFetch = globalThis.fetch, logs = [];
  t.mock.method(console, "warn", (...args) => logs.push(args));
  try {
    globalThis.fetch = async () => Response.json({ error: "invalid_grant" }, { status: 400 });
    await assert.rejects(osuRequest(env, "/oauth/token"), e => e.status === 400 && /HTTP 400; invalid_grant/.test(e.message));
    assert.deepEqual(logs, [["osu_verification_failed", { operation: "token exchange", reason: "HTTP", status: 400, code: "invalid_grant", format: "json" }]]);
    assert.deepEqual((await read(env, "SELECT failures,blocked_until FROM osu_api_state")).results, [{ failures: 0, blocked_until: 0 }]);
  } finally { globalThis.fetch = originalFetch; }
});

for (const [name, makeResponse, status, code, format] of [
  ["HTML rejection", () => new Response("private-response-data", { status: 403, headers: { "Content-Type": "text/html" } }), 403, null, "html"],
  ["unrecognised error", () => Response.json({ error: "private-response-data" }, { status: 401 }), 401, null, "json"],
  ["oversized response", () => Response.json({ padding: "x".repeat(4096), error: "invalid_client" }, { status: 401 }), 401, null, "json"],
  ["malformed error response", () => new Response("private-response-data", { status: 500, headers: { "Content-Type": "application/json" } }), 500, null, "json"],
]) test(`HTTP diagnostics preserve the original failure for ${name}`, async t => {
  const env = await setup(), originalFetch = globalThis.fetch, logs = [];
  t.mock.method(console, "warn", (...args) => logs.push(args));
  try {
    globalThis.fetch = async () => makeResponse();
    await assert.rejects(osuRequest(env, "/oauth/token"), e => {
      assert.equal(e.status, 503);
      assert.match(e.message, new RegExp(`HTTP ${status}`));
      assert.ok(!e.message.includes("private-response-data"));
      return true;
    });
    assert.deepEqual(logs, [["osu_verification_failed", { operation: "token exchange", reason: "HTTP", status, code, format }]]);
    assert.deepEqual((await read(env, "SELECT failures,lease_until FROM osu_api_state")).results, [{ failures: 1, lease_until: 0 }]);
  } finally { globalThis.fetch = originalFetch; }
});

for (const [name, failure, reason] of [
  ["network error", () => { throw new TypeError("private-network-details"); }, "request error"],
  ["timeout", () => { throw new DOMException("private-network-details", "TimeoutError"); }, "timeout"],
  ["invalid JSON", () => new Response("private-network-details"), "invalid JSON"],
]) test(`account lookup diagnostics classify ${name} without logging exception contents`, async t => {
  const env = await setup(), originalFetch = globalThis.fetch, logs = [];
  t.mock.method(console, "warn", (...args) => logs.push(args));
  try {
    globalThis.fetch = async () => failure();
    await assert.rejects(osuRequest(env, "/api/v2/me"), e => {
      assert.equal(e.status, 503);
      assert.ok(e.message.includes(`account lookup failed (${reason})`));
      assert.ok(!e.message.includes("private-network-details"));
      return true;
    });
    assert.deepEqual(logs, [["osu_verification_failed", { operation: "account lookup", reason, status: null, code: null, format: null }]]);
    assert.deepEqual((await read(env, "SELECT failures,lease_until FROM osu_api_state")).results, [{ failures: 1, lease_until: 0 }]);
  } finally { globalThis.fetch = originalFetch; }
});

test("a terminated Worker's request lease expires, and cleanup preserves live lookup cooldowns", async () => {
  const env = await setup(), originalFetch = globalThis.fetch, originalNow = Date.now;
  let time = originalNow(), calls = 0;
  await env.DB.prepare("UPDATE osu_api_state SET lease_owner='terminated',lease_until=?").bind(time + 30000).run();
  await env.DB.prepare("INSERT INTO osu_lookup_locks VALUES('expired','owner',?),('active','owner',?)").bind(time - 1, time + 60000).run();
  try {
    Date.now = () => time;
    globalThis.fetch = async () => { calls++; return Response.json({}); };
    await assert.rejects(osuRequest(env, "/api/v2/beatmaps/1"), e => e.status === 429);
    assert.equal(calls, 0);
    time += 30000;
    await osuRequest(env, "/api/v2/beatmaps/1");
    await worker.scheduled({}, env);
    assert.equal(calls, 1);
    assert.deepEqual((await read(env, "SELECT key FROM osu_lookup_locks")).results, [{ key: "active" }]);
  } finally { globalThis.fetch = originalFetch; Date.now = originalNow; }
});

test("the same player, map and score ID have independent PBs, totals and ranks per ruleset", async () => {
  const env = await setup(); enableTestRuleset(env);
  const sticks = scopedPath("sticks", "scores"), other = scopedPath("test-mode", "scores");
  assert.equal((await call(env, sticks, score())).status, 200);
  assert.equal((await call(env, other, score({ pp: 700 }))).status, 200);
  assert.equal((await call(env, sticks, score({ pp: 150 }))).status, 200);
  assert.equal((await call(env, other, score({ pp: 50 }))).status, 200);
  const a = await (await call(env, scopedPath("sticks", "leaderboard"))).json();
  const b = await (await call(env, scopedPath("test-mode", "leaderboard"))).json();
  assert.equal(a.ruleset, "sticks"); assert.equal(b.ruleset, "test-mode");
  assert.deepEqual(a.entries, [{ rank: 1, userId: 42, username: "Player", pp: 150 }]);
  assert.deepEqual(b.entries, [{ rank: 1, userId: 42, username: "Player", pp: 700 }]);
  assert.equal((await read(env, "SELECT * FROM scores")).results.length, 2);
  assert.equal((await read(env, "SELECT * FROM users")).results.length, 1);
});

test("whitelisting or removing a set affects only the specified ruleset", async () => {
  const env = await setup(); enableTestRuleset(env); await addMap(env, 2, 20, 0);
  const payload = score({ beatmapId: 2, beatmapSetId: 20 });
  await env.DB.prepare("INSERT INTO whitelisted_sets VALUES('sticks',20)").bind().run();
  assert.equal((await (await call(env, scopedPath("sticks", "scores"), payload)).json()).accepted, true);
  assert.equal((await (await call(env, scopedPath("test-mode", "scores"), payload)).json()).accepted, false);
  await env.DB.prepare("INSERT INTO whitelisted_sets VALUES('test-mode',20)").bind().run();
  assert.equal((await (await call(env, scopedPath("test-mode", "scores"), { ...payload, pp: 200 })).json()).accepted, true);
  await env.DB.prepare("DELETE FROM whitelisted_sets WHERE ruleset='sticks' AND set_id=20").bind().run();
  assert.deepEqual((await (await call(env, scopedPath("sticks", "policy"))).json()).whitelistedSets, []);
  assert.deepEqual((await (await call(env, scopedPath("test-mode", "policy"))).json()).whitelistedSets, [20]);
  assert.deepEqual((await (await call(env, scopedPath("sticks", "leaderboard"))).json()).entries, []);
  assert.equal((await (await call(env, scopedPath("test-mode", "leaderboard"))).json()).entries[0].pp, 200);
  await env.DB.prepare("INSERT INTO whitelisted_sets VALUES('sticks',20)").bind().run();
  assert.equal((await (await call(env, scopedPath("sticks", "leaderboard"))).json()).entries[0].pp, 100);
  assert.equal((await (await call(env, scopedPath("test-mode", "leaderboard"))).json()).entries[0].pp, 200);
});

test("generation, difficulty version and judgement layout are validated per ruleset", async () => {
  const env = await setup(); enableTestRuleset(env, { generation: 7, difficultyVersion: 123, judgementCount: 2 });
  const otherScore = score({ generation: 7, difficultyVersion: 123, judgements: [100, 5] });
  assert.equal((await call(env, scopedPath("test-mode", "scores"), score())).status, 400);
  assert.equal((await call(env, scopedPath("sticks", "scores"), otherScore)).status, 400);
  assert.equal((await call(env, scopedPath("test-mode", "scores"), otherScore)).status, 200);
  assert.equal((await call(env, scopedPath("sticks", "scores"), score())).status, 200);
  const policy = await (await call(env, scopedPath("test-mode", "policy"))).json();
  assert.equal(policy.generation, 7); assert.equal(policy.difficultyVersion, 123);
  assert.deepEqual((await read(env, "SELECT ruleset,length(judgements) AS size FROM scores ORDER BY ruleset")).results,
    [{ ruleset: "sticks", size: 64 }, { ruleset: "test-mode", size: 8 }]);
});

test("a generation change hides only that ruleset's old rankings", async () => {
  const env = await setup(); enableTestRuleset(env);
  await call(env, scopedPath("sticks", "scores"), score());
  await call(env, scopedPath("test-mode", "scores"), score({ pp: 200 }));
  env.RULESETS.sticks.generation = 2;
  assert.equal((await call(env, scopedPath("sticks", "scores"), score())).status, 400);
  assert.deepEqual((await (await call(env, scopedPath("sticks", "leaderboard"))).json()).entries, []);
  assert.equal((await (await call(env, scopedPath("test-mode", "leaderboard"))).json()).entries[0].pp, 200);
  await call(env, scopedPath("sticks", "scores"), score({ generation: 2, pp: 50 }));
  assert.equal((await (await call(env, scopedPath("sticks", "leaderboard"))).json()).entries[0].pp, 50);
  assert.equal((await read(env, "SELECT * FROM scores")).results.length, 3);
});

test("unknown or ambiguous namespaces fail closed without touching osu! or storing scores", async () => {
  const env = await setup(), originalFetch = globalThis.fetch;
  try {
    globalThis.fetch = () => { throw new Error("Must not contact osu!"); };
    for (const id of ["test-mode", "constructor", "__proto__", "Sticks", "sticks%2Ftest"])
      for (const resource of ["policy", "leaderboard", "scores"])
        assert.equal((await call(env, scopedPath(id, resource), resource === "scores" ? score() : undefined)).status, 404);
    for (const resource of ["policy", "leaderboard", "scores"])
      assert.equal((await call(env, `/v1/${resource}`, resource === "scores" ? score() : undefined)).status, 404);
    assert.equal((await call(env, scopedPath("sticks", "scores"), score({ ruleset: "test-mode" }))).status, 400);
    assert.equal((await call(env, scopedPath("sticks", "scores?ruleset=test-mode"), score())).status, 400);
    assert.equal((await call(env, scopedPath("sticks", "leaderboard?offset=0&offset=1"))).status, 400);
    assert.equal((await read(env, "SELECT * FROM scores")).results.length, 0);
  } finally { globalThis.fetch = originalFetch; }
});

test("each ruleset gets its own weighted top 100", async () => {
  const env = await setup(); enableTestRuleset(env);
  for (let id = 1; id <= 105; id++) {
    if (id > 1) await addMap(env, id);
    for (const [ruleset, pp] of [["sticks", 100], ["test-mode", 200]])
      await env.DB.prepare(upsertScoreSql).bind(...scoreValues(score({ beatmapId: id, pp }), ruleset)).run();
  }
  for (const [ruleset, pp] of [["sticks", 100], ["test-mode", 200]]) {
    await env.DB.prepare(updateTotalSql).bind(42, 1, ruleset).run();
    const ranking = await (await call(env, scopedPath(ruleset, "leaderboard"))).json();
    assert.ok(Math.abs(ranking.entries[0].pp - pp * (1 - 0.95 ** 100) / 0.05) < 1e-8);
  }
});

test("shared map checksum invalidation affects both rulesets without mixing their replacement PBs", async () => {
  const env = await setup(); enableTestRuleset(env);
  await call(env, scopedPath("sticks", "scores"), score());
  await call(env, scopedPath("test-mode", "scores"), score({ pp: 200 }));
  const changed = "f".repeat(32);
  await env.DB.prepare("UPDATE maps SET md5=? WHERE id=1").bind(unhex(changed)).run();
  for (const ruleset of ["sticks", "test-mode"])
    assert.deepEqual((await (await call(env, scopedPath(ruleset, "leaderboard"))).json()).entries, []);
  await call(env, scopedPath("sticks", "scores"), score({ sourceMd5: changed, pp: 50 }));
  assert.equal((await (await call(env, scopedPath("sticks", "leaderboard"))).json()).entries[0].pp, 50);
  assert.deepEqual((await (await call(env, scopedPath("test-mode", "leaderboard"))).json()).entries, []);
  assert.equal((await read(env, "SELECT pp FROM scores WHERE ruleset='test-mode'")).results[0].pp, 200);
});

test("manual account bans cover all rulesets, and unbanning restores each ranking", async () => {
  const env = await setup(); enableTestRuleset(env);
  for (const ruleset of ["sticks", "test-mode"]) await call(env, scopedPath(ruleset, "scores"), score());
  await env.DB.prepare("INSERT INTO banned_users VALUES(42)").bind().run();
  for (const ruleset of ["sticks", "test-mode"]) {
    assert.equal((await call(env, scopedPath(ruleset, "scores"), score())).status, 403);
    assert.deepEqual((await (await call(env, scopedPath(ruleset, "leaderboard"))).json()).entries, []);
  }
  await env.DB.prepare("DELETE FROM banned_users WHERE user_id=42").bind().run();
  for (const ruleset of ["sticks", "test-mode"])
    assert.equal((await (await call(env, scopedPath(ruleset, "leaderboard"))).json()).entries[0].pp, 100);
});

test("rulesets share successful beatmap lookups instead of multiplying osu! requests", async () => {
  const env = await setup(), originalFetch = globalThis.fetch; enableTestRuleset(env);
  await cachePublicToken(env);
  let calls = 0;
  try {
    globalThis.fetch = async () => { calls++; return Response.json(mapMetadata(2)); };
    for (const ruleset of ["sticks", "test-mode"])
      assert.equal((await call(env, scopedPath(ruleset, "scores"), score({ beatmapId: 2 }))).status, 200);
    assert.equal(calls, 1);
  } finally { globalThis.fetch = originalFetch; }
});
