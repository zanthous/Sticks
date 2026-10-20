import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { gzipSync, gunzipSync } from "node:zlib";
import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { Miniflare, convertV4MiniflareOptions, Log, LogLevel } from "miniflare";
import { scoreValues, unhex, upsertScoreSql } from "../src/policy.mjs";

const config = JSON.parse(readFileSync(new URL("../wrangler.jsonc", import.meta.url), "utf8").replace(/^\s*\/\/[^\n]*$/gm, ""));
const origin = "https://leaderboard.test";
const verifier = "b".repeat(64);
const migrations = new URL("../migrations/", import.meta.url);
const schema = readdirSync(migrations).filter(name => name.endsWith(".sql")).sort()
  .map(name => readFileSync(new URL(name, migrations), "utf8")).join("\n");

async function setup(t, upstream, options = {}) {
  const requests = [];
  const runtime = new Miniflare(convertV4MiniflareOptions({
    modulesRoot: fileURLToPath(new URL("../src/", import.meta.url)),
    modules: ["worker.mjs", "policy.mjs", "osu-api.mjs", "http.mjs", "profiles.mjs", "replays.mjs"].map(name => ({
      type: "ESModule", path: fileURLToPath(new URL("../src/" + name, import.meta.url)),
    })),
    compatibilityDate: config.compatibility_date,
    compatibilityFlags: config.compatibility_flags ?? [],
    bindings: { PUBLIC_ORIGIN: origin, OSU_CLIENT_ID: "1", OSU_CLIENT_SECRET: "test-secret",
      RULESETS: config.vars.RULESETS, REPLAY_POLICY: options.replayPolicy ?? {} },
    d1Databases: ["DB"],
    r2Buckets: ["REPLAYS"],
    ratelimits: Object.fromEntries(config.ratelimits.map(r => [r.name, {
      simple: { ...r.simple, ...(options.edgeLimit ? { limit: options.edgeLimit } : {}) }, namespace_id: r.namespace_id,
    }])),
    // All outgoing requests stay in this fixture. Never contact osu! or production data.
    outboundService: async request => {
      requests.push(request.url);
      return upstream(request);
    },
    cf: false,
    log: new Log(LogLevel.ERROR),
  }));
  t.after(async () => { await runtime.dispose(); });
  const db = await runtime.getD1Database("DB");
  // D1 exec splits on newlines. Keep complete migration statements and triggers together.
  await db.exec(schema.replace(/^\s*--[^\n]*$/gm, "").replace(/\r?\n/g, " "));
  const call = (path, body, headers = {}) => runtime.dispatchFetch(origin + path, {
    method: body === undefined ? "GET" : "POST", redirect: "manual",
    headers: { "Content-Type": "application/json", ...headers },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  return { runtime, requests, db, call };
}

const replayToken = "d".repeat(64);
const runtimeReplayId = n => n.toString(16).padStart(32, "0");
const runtimeReplayPath = n => `/v1/rulesets/sticks/users/42/scores/${n}/${runtimeReplayId(n)}/replay`;

function runtimeRecording() {
  const raw = Buffer.alloc(12 + 30 * 18000);
  raw.writeUInt32LE(0x53544b52); raw.writeInt32LE(5, 4); raw.writeFloatLE(0.97, 8);
  for (let i = 0; i < 18000; i++) {
    raw.writeDoubleLE(i * 16.67, 12 + i * 30);
    raw.writeFloatLE(Math.sin(i / 5), 20 + i * 30);
  }
  return gzipSync(raw);
}

async function seedRuntimePlayer(db) {
  await db.prepare("INSERT INTO users VALUES(42,'Replay test')").run();
  await db.prepare("INSERT INTO sessions VALUES(?,?,?)")
    .bind(createHash("sha256").update(replayToken).digest("hex"), 42, Math.floor(Date.now() / 1000) + 3600).run();
}

async function seedRuntimeScore(db, n, pp) {
  const md5 = "0123456789abcdef0123456789abcdef", rule = config.vars.RULESETS.sticks;
  await db.prepare("INSERT INTO maps VALUES(?,?,?,?,?,?)")
    .bind(n, 10, unhex(md5), 0, 1, Math.floor(Date.now() / 1000)).run();
  const score = { scoreId: runtimeReplayId(n), userId: 42, beatmapId: n, beatmapSetId: 10, sourceMd5: md5,
    playedAt: Date.now(), generation: rule.generation, difficultyVersion: rule.difficultyVersion,
    mods: 0, rate: 1, pp, accuracy: 0.99, maxCombo: 100, totalScore: 990000, activationThreshold: 0.97,
    judgements: [100, 2, 0, 3, 100, 2, 0, 3, 0, 0, 0, 1, 100, 2, 10, 1] };
  await db.prepare(upsertScoreSql).bind(...scoreValues(score, "sticks")).run();
}

const runtimeUpload = (runtime, n, data = runtimeRecording()) => runtime.dispatchFetch(origin + runtimeReplayPath(n), {
  method: "PUT", headers: { "Content-Type": "application/gzip", Authorization: `Bearer ${replayToken}` }, body: data,
});

test("Workers runtime validates, stores and downloads a five-minute replay using real D1/R2", { timeout: 15000 }, async t => {
  const { runtime, db, call, requests } = await setup(t, () => { throw new Error("No upstream requests allowed"); });
  await seedRuntimePlayer(db); await seedRuntimeScore(db, 1, 100);
  const bytes = runtimeRecording(), response = await runtimeUpload(runtime, 1, bytes);
  assert.equal(response.status, 201, await response.clone().text());
  const uploaded = await response.json();
  const downloaded = await call(uploaded.downloadPath);
  assert.equal(downloaded.status, 200);
  const packed = Buffer.from(await downloaded.arrayBuffer()), length = packed.readUInt32LE(4);
  assert.equal(packed.toString("ascii", 0, 4), "ZKR1");
  assert.deepEqual(gunzipSync(packed.subarray(8 + length)), gunzipSync(bytes));
  assert.equal(createHash("sha256").update(packed).digest("hex"), uploaded.sha256);
  const profile = await readOk(await call("/v1/rulesets/sticks/users/42/top"));
  assert.equal(profile.entries[0].missCount, 4);
  assert.equal(profile.entries[0].statistics.tracking.miss, 2);
  assert.equal(profile.entries[0].replay.available, true);
  assert.deepEqual(requests, []);
  await db.prepare("INSERT INTO banned_users VALUES(42)").run();
  assert.equal((await call(uploaded.downloadPath)).status, 404);
  await (await runtime.getWorker()).scheduled();
  const bucket = await runtime.getR2Bucket("REPLAYS");
  assert.equal((await bucket.list()).objects.length, 0);
  assert.equal((await db.prepare("SELECT bytes FROM replay_storage").first()).bytes, 0);
});

test("Workers runtime concurrent uploads respect the global storage reservation", { timeout: 15000 }, async t => {
  const bytes = runtimeRecording();
  const { runtime, db } = await setup(t, () => Response.json({}), {
    replayPolicy: { maxStoredBytes: bytes.length + 10000 },
  });
  await seedRuntimePlayer(db);
  for (let n = 1; n <= 3; n++) await seedRuntimeScore(db, n, 100);
  const responses = await Promise.all([1, 2, 3].map(n => runtimeUpload(runtime, n, bytes)));
  assert.deepEqual(responses.map(r => r.status).sort(), [201, 507, 507]);
  assert.equal((await (await runtime.getR2Bucket("REPLAYS")).list()).objects.length, 1);
});

test("Workers runtime uses the configured edge limiter and returns Retry-After", { timeout: 15000 }, async t => {
  const { call } = await setup(t, () => Response.json({}), { edgeLimit: 2 });
  assert.equal((await call("/v1/rulesets/sticks/policy")).status, 200);
  assert.equal((await call("/v1/rulesets/sticks/policy")).status, 200);
  const limited = await call("/v1/rulesets/sticks/policy");
  assert.equal(limited.status, 429); assert.equal(limited.headers.get("retry-after"), "60");
});

async function readOk(response) {
  const text = await response.text();
  assert.equal(response.status, 200, text);
  return JSON.parse(text);
}

async function beginLogin(call) {
  const started = await readOk(await call("/auth/start", {
    userId: 42, challenge: createHash("sha256").update(verifier).digest("hex"),
  }));
  const redirect = await call(new URL(started.url).pathname + new URL(started.url).search);
  assert.equal(redirect.status, 302);
  const target = new URL(redirect.headers.get("location"));
  assert.equal(target.origin, "https://osu.ppy.sh");
  return {
    id: started.id,
    callback: `/auth/callback?state=${target.searchParams.get("state")}&code=test-code`,
    cookie: redirect.headers.get("set-cookie").split(";")[0],
  };
}

test("Workers runtime completes OAuth, verifies a map and stores the score using real D1", { timeout: 30000 }, async t => {
  const md5 = "0123456789abcdef0123456789abcdef";
  const { requests, db, call } = await setup(t, async request => {
    const url = new URL(request.url);
    assert.equal(url.origin, "https://osu.ppy.sh");
    if (url.pathname === "/oauth/token") {
      assert.equal(request.method, "POST");
      const body = await request.json();
      assert.equal(body.client_secret, "test-secret");
      assert.ok(["authorization_code", "client_credentials"].includes(body.grant_type));
      return Response.json({ access_token: body.grant_type === "authorization_code" ? "player-token" : "public-token", expires_in: 3600 });
    }
    if (url.pathname === "/api/v2/me") {
      assert.equal(request.headers.get("authorization"), "Bearer player-token");
      return Response.json({ id: 42, username: "Runtime test" });
    }
    assert.equal(url.pathname, "/api/v2/beatmaps/1");
    assert.equal(request.headers.get("authorization"), "Bearer public-token");
    return Response.json({ id: 1, beatmapset_id: 10, checksum: md5, mode_int: 0, ranked: 1,
      version: "Expert", beatmapset: { title: "Runtime song", artist: "Runtime artist" } });
  });

  const login = await beginLogin(call);
  const callback = await call(login.callback, undefined, { Cookie: login.cookie });
  assert.equal(callback.status, 200, await callback.text());
  const session = await readOk(await call("/auth/complete", { id: login.id, verifier }));
  assert.equal(session.userId, 42);
  const policy = config.vars.RULESETS.sticks;
  const accepted = await readOk(await call("/v1/rulesets/sticks/scores", {
    scoreId: "1".repeat(32), userId: 42, beatmapId: 1, beatmapSetId: 10, sourceMd5: md5,
    playedAt: Date.now() - 10000, generation: policy.generation, difficultyVersion: policy.difficultyVersion,
    mods: 0, rate: 1, pp: 100, accuracy: 0.98, maxCombo: 500, totalScore: 980000, activationThreshold: 0.97,
    judgements: [100, 2, 0, 0, 100, 2, 0, 0, 0, 0, 0, 0, 100, 0, 2, 0],
  }, { Authorization: `Bearer ${session.token}` }));
  assert.deepEqual(accepted, { ruleset: "sticks", accepted: true });
  const ranking = await readOk(await call("/v1/rulesets/sticks/leaderboard"));
  assert.deepEqual(ranking.entries, [{ rank: 1, userId: 42, username: "Runtime test", pp: 100 }]);
  const top = await readOk(await call("/v1/rulesets/sticks/users/42/top"));
  assert.equal(top.entries[0].title, "Runtime song");
  assert.equal(top.entries[0].difficulty, "Expert");
  assert.equal((await db.prepare("SELECT count(*) AS n FROM service_tokens WHERE token='player-token'").first()).n, 0);
  assert.deepEqual(requests, ["/oauth/token", "/api/v2/me", "/oauth/token", "/api/v2/beatmaps/1"].map(path => "https://osu.ppy.sh" + path));
});

for (const status of [301, 302, 303, 307, 308]) {
  test(`Workers runtime rejects HTTP ${status} without forwarding OAuth credentials`, { timeout: 15000 }, async t => {
    const { requests, db, call } = await setup(t, async request => {
      if (request.url === "https://osu.ppy.sh/oauth/token")
        return new Response(null, { status, headers: { location: "https://unexpected.test/receive" } });
      return Response.json({});
    });
    const login = await beginLogin(call);
    const response = await call(login.callback, undefined, { Cookie: login.cookie });
    assert.equal(response.status, 503);
    assert.match((await response.json()).error, new RegExp(`token exchange failed \\(HTTP ${status}\\)`));
    assert.deepEqual(requests, ["https://osu.ppy.sh/oauth/token"]);
    const state = await db.prepare("SELECT failures,lease_until FROM osu_api_state").first();
    assert.deepEqual(state, { failures: 1, lease_until: 0 });
    assert.equal((await db.prepare("SELECT verified FROM logins WHERE id=?").bind(login.id).first()).verified, 0);
    assert.deepEqual(await readOk(await call("/auth/complete", { id: login.id, verifier })), { pending: true });
  });
}
