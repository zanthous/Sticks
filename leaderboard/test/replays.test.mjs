import test from "node:test";
import assert from "node:assert/strict";
import { webcrypto, createHash } from "node:crypto";
import { gzipSync, gunzipSync } from "node:zlib";
import { readFileSync } from "node:fs";
import worker from "../src/worker.mjs";
import { digest, scoreValues, unhex, updateTotalSql, upsertScoreSql } from "../src/policy.mjs";
import { deleteGarbage, maintainReplays, prunePlayer, replayPolicy } from "../src/replays.mjs";
import { limit } from "../src/http.mjs";
import { Database } from "./database.mjs";
globalThis.crypto ??= webcrypto;

const token = "a".repeat(64), md5 = "0123456789abcdef0123456789abcdef";
const scoreId = n => n.toString(16).padStart(32, "0");
const query = (env, sql, ...values) => env.DB.prepare(sql).bind(...values);
const time = () => Math.floor(Date.now() / 1000);
const path = (map = 1, id = scoreId(map), user = 42, rule = "sticks") =>
  `/v1/rulesets/${rule}/users/${user}/scores/${map}/${id}/replay`;

class Bucket {
  objects = new Map();
  dates = new Map();
  puts = 0;
  async put(key, bytes) { this.puts++; this.objects.set(key, Uint8Array.from(bytes)); this.dates.set(key, new Date()); }
  async get(key) {
    const bytes = this.objects.get(key);
    return bytes ? { body: bytes, size: bytes.length } : null;
  }
  async delete(keys) { for (const key of Array.isArray(keys) ? keys : [keys]) this.objects.delete(key); }
  async list() { return { truncated: false, objects: [...this.objects.keys()].map(key => ({ key, uploaded: this.dates.get(key) })) }; }
}

function recording(frames = 180, alter = () => {}) {
  const raw = Buffer.alloc(12 + frames * 30);
  raw.writeUInt32LE(0x53544b52); raw.writeInt32LE(5, 4); raw.writeFloatLE(0.97, 8);
  for (let i = 0; i < frames; i++) {
    raw.writeDoubleLE(i * 16.67, 12 + i * 30);
    raw.writeFloatLE(Math.sin(i / 10), 20 + i * 30);
  }
  alter(raw);
  return gzipSync(raw);
}

function request(env, url, method = "GET", body, headers = {}) {
  return worker.fetch(new Request(env.PUBLIC_ORIGIN + url, {
    method, headers: { Authorization: `Bearer ${token}`, ...headers },
    ...(body === undefined ? {} : { body }),
  }), env);
}
const upload = (env, map = 1, bytes = recording(), headers = {}, id = scoreId(map), user = 42) =>
  request(env, path(map, id, user), "PUT", bytes, { "Content-Type": "application/gzip", ...headers });
async function ok(response, status = 200) {
  const text = await response.text();
  assert.equal(response.status, status, text);
  return JSON.parse(text);
}

async function addScore(env, mapId, pp = 100, overrides = {}) {
  await query(env, "INSERT OR IGNORE INTO maps VALUES(?,?,?,?,?,?)", mapId, 10, unhex(md5), 0, 1, time()).run();
  const score = { scoreId: scoreId(mapId), userId: 42, beatmapId: mapId, beatmapSetId: 10,
    sourceMd5: md5, playedAt: 100000 + mapId, generation: 1, difficultyVersion: 202609163,
    mods: 0, rate: 1, pp, accuracy: 0.97, maxCombo: 100, totalScore: 970000,
    activationThreshold: 0.97, judgements: [100, 3, 2, 4, 100, 5, 0, 4, 10, 2, 1, 3, 20, 6, 10, 2], ...overrides };
  await query(env, upsertScoreSql, ...scoreValues(score, overrides.ruleset ?? "sticks")).run();
  return score;
}

async function setup(policy = {}) {
  const env = { DB: new Database(), REPLAYS: new Bucket(), PUBLIC_ORIGIN: "https://test.example",
    RULESETS: { sticks: { generation: 1, difficultyVersion: 202609163, judgementCount: 16 } },
    REPLAY_POLICY: policy };
  for (const binding of ["API_RATE_LIMITER", "READ_RATE_LIMITER", "DOWNLOAD_RATE_LIMITER"])
    env[binding] = { limit: async () => ({ success: true }) };
  await query(env, "INSERT INTO users VALUES(42,'Player'),(43,'Other')").run();
  await query(env, "INSERT INTO sessions VALUES(?,?,?)", await digest(token), 42, time() + 3600).run();
  await addScore(env, 1);
  return env;
}

test("profile returns ordered paginated PBs, independent misses, titles and no upstream calls", async () => {
  const env = await setup();
  await addScore(env, 2, 120);
  await query(env, "INSERT INTO map_details VALUES(2,'Song','Artist','Difficulty')").run();
  await query(env, updateTotalSql, 42, 1, "sticks").run();
  const page = await ok(await request(env, "/v1/rulesets/sticks/users/42/top?limit=1"));
  assert.equal(page.total, 2); assert.equal(page.nextOffset, 1);
  assert.equal(page.player.pp, 215);
  const play = page.entries[0];
  assert.equal(play.rank, 1); assert.equal(play.beatmapId, 2); assert.equal(play.title, "Song");
  assert.equal(play.missCount, 7, "4 head misses plus 3 click misses, not 11 with duplicate aim misses");
  assert.deepEqual(play.statistics.tracking, { hit: 20, miss: 6 });
  assert.deepEqual(play.statistics.tail, { hit: 10, miss: 2 });
  assert.equal(play.judgements.length, 16);
  assert.equal(play.replay.available, false); assert.equal(play.replay.eligible, true);
  const second = await ok(await request(env, "/v1/rulesets/sticks/users/42/top?offset=1&limit=1"));
  assert.equal(second.entries[0].rank, 2); assert.equal(second.entries[0].weightedPp, 95);
  assert.equal(second.entries[0].title, null); assert.equal(second.nextOffset, null);
  for (const suffix of ["?offset=100", "?offset=-1", "?limit=0", "?limit=101", "?limit=2&limit=3", "?foo=x"])
    assert.equal((await request(env, "/v1/rulesets/sticks/users/42/top" + suffix)).status, 400);
  assert.equal((await request(env, "/v1/rulesets/sticks/users/99/top")).status, 404);
});

test("upload/download is lossless, self-contained, hashed and idempotent", async () => {
  const env = await setup(), bytes = recording();
  const result = await ok(await upload(env, 1, bytes), 201);
  const response = await request(env, result.downloadPath);
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-disposition"), /attachment.*\.zkr/);
  const packed = Buffer.from(await response.arrayBuffer());
  assert.equal(packed.toString("ascii", 0, 4), "ZKR1");
  const size = packed.readUInt32LE(4);
  const metadata = JSON.parse(packed.subarray(8, 8 + size));
  assert.equal(metadata.ruleset, "sticks"); assert.equal(metadata.player.userId, 42);
  assert.equal(metadata.score.sourceMd5, md5); assert.equal(metadata.score.missCount, 7);
  assert.equal(metadata.score.generation, 1); assert.equal(metadata.recording.version, 5);
  assert.deepEqual(gunzipSync(packed.subarray(8 + size)), gunzipSync(bytes));
  assert.equal(result.sha256, createHash("sha256").update(packed).digest("hex"));
  assert.equal(response.headers.get("x-replay-sha256"), result.sha256);
  assert.equal((await ok(await upload(env))).alreadyStored, true);
  assert.equal(env.REPLAYS.puts, 1);
  assert.equal((await query(env, "SELECT bytes FROM replay_storage").first()).bytes, packed.length);
  const profile = await ok(await request(env, "/v1/rulesets/sticks/users/42/top"));
  assert.equal(profile.entries[0].replay.sha256, result.sha256);
  await query(env, "INSERT INTO banned_users VALUES(42)").run();
  assert.equal((await request(env, result.downloadPath)).status, 404);
  assert.deepEqual(gunzipSync(packed.subarray(8 + size)), gunzipSync(bytes), "download remains usable after remote removal");
});

test("only the owner of a current eligible score can upload", async () => {
  const env = await setup();
  assert.equal((await upload(env, 1, recording(), { Authorization: "" })).status, 401);
  assert.equal((await upload(env, 1, recording(), {}, scoreId(1), 43)).status, 403);
  assert.equal((await upload(env, 1, recording(), {}, scoreId(99))).status, 409);
  assert.equal((await upload(env, 99)).status, 409);
  await query(env, "INSERT INTO banned_users VALUES(42)").run();
  assert.equal((await upload(env)).status, 403);
  assert.equal((await request(env, "/v1/rulesets/sticks/users/42/top")).status, 404);
  assert.equal(env.REPLAYS.objects.size, 0);
});

test("actual top 100 is enforced, including stable ties and missing higher-ranked replays", async () => {
  const env = await setup();
  for (let i = 2; i <= 101; i++) await addScore(env, i);
  const page = await ok(await request(env, "/v1/rulesets/sticks/users/42/top?limit=100"));
  assert.equal(page.entries.length, 100); assert.equal(page.entries[99].beatmapId, 100);
  assert.equal((await upload(env, 101)).status, 409);
  assert.equal((await upload(env, 100)).status, 201);
  await addScore(env, 101, 200);
  await prunePlayer(env, { id: "sticks", generation: 1 }, 42);
  await deleteGarbage(env);
  assert.equal(env.REPLAYS.objects.size, 0);
  assert.equal((await request(env, path(100))).status, 404);
});

test("lowered cap and generation changes deny immediately and clean up on maintenance", async () => {
  const env = await setup();
  await addScore(env, 2, 200);
  await ok(await upload(env, 1), 201); await ok(await upload(env, 2), 201);
  env.REPLAY_POLICY.retainedPerPlayer = 1;
  assert.equal((await request(env, path(1))).status, 404);
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.size, 1);
  env.RULESETS.sticks.generation = 2;
  assert.equal((await request(env, path(2))).status, 404);
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.size, 0);
  assert.equal((await query(env, "SELECT bytes FROM replay_storage").first()).bytes, 0);
});

for (const reason of ["checksum", "whitelist", "ban", "deleted", "replaced"]) {
  test(`${reason} removes access immediately and scheduled cleanup deletes the replay`, async () => {
    const env = await setup();
    if (reason === "whitelist") {
      await query(env, "UPDATE maps SET ranked=0").run();
      await query(env, "INSERT INTO whitelisted_sets VALUES('sticks',10)").run();
    }
    await ok(await upload(env), 201);
    if (reason === "checksum") await query(env, "UPDATE maps SET md5=?", unhex("f".repeat(32))).run();
    if (reason === "whitelist") await query(env, "DELETE FROM whitelisted_sets").run();
    if (reason === "ban") await query(env, "INSERT INTO banned_users VALUES(42)").run();
    if (reason === "deleted") await query(env, "DELETE FROM scores").run();
    if (reason === "replaced") await addScore(env, 1, 200, { scoreId: scoreId(200) });
    assert.equal((await request(env, path())).status, 404);
    await worker.scheduled({}, env);
    assert.equal(env.REPLAYS.objects.size, 0);
    assert.equal((await query(env, "SELECT bytes FROM replay_storage").first()).bytes, 0);
  });
}

test("bad recordings, size limits and decompression bombs do not reach R2", async () => {
  const env = await setup({ maxRawBytes: 10000, maxCompressedBytes: 5000 });
  for (const bytes of [Buffer.from("not gzip"), recording(1, r => r.writeInt32LE(99, 4)),
    recording(2, r => r.writeDoubleLE(-1, 42)), recording(1, r => r.writeFloatLE(NaN, 20)),
    recording(1, r => r.writeFloatLE(0.9, 8)), recording(1, r => { r[36] = 2; }),
    recording().subarray(0, 30)]) {
    assert.equal((await upload(env, 1, bytes)).status, 400);
  }
  assert.equal((await upload(env, 1, Buffer.alloc(5001))).status, 413);
  assert.equal((await upload(env, 1, recording(500))).status, 413);
  assert.equal((await upload(env, 1, recording(), { "Content-Encoding": "gzip" })).status, 415);
  assert.equal(env.REPLAYS.puts, 0);
});

test("global storage reservation cannot be exceeded by concurrent uploads", async () => {
  const env = await setup();
  await ok(await upload(env), 201);
  const size = (await query(env, "SELECT bytes FROM replay_storage").first()).bytes;
  await query(env, "UPDATE replays SET state='deleting'").run(); await deleteGarbage(env);
  env.REPLAY_POLICY.maxStoredBytes = size + 100;
  await addScore(env, 2);
  const responses = await Promise.all([upload(env, 1), upload(env, 2)]);
  assert.deepEqual(responses.map(r => r.status).sort(), [201, 507]);
  assert.equal(env.REPLAYS.objects.size, 1);
  assert.ok((await query(env, "SELECT bytes FROM replay_storage").first()).bytes <= size + 100);
});

test("failed PUT and failed delete retain accounting until cleanup really succeeds", async () => {
  const env = await setup();
  const put = env.REPLAYS.put.bind(env.REPLAYS), remove = env.REPLAYS.delete.bind(env.REPLAYS);
  env.REPLAYS.put = async (...args) => { await put(...args); throw new Error("connection failed after commit"); };
  env.REPLAYS.delete = async () => { throw new Error("storage temporarily unavailable"); };
  assert.equal((await upload(env)).status, 503);
  assert.equal(env.REPLAYS.objects.size, 1);
  assert.ok((await query(env, "SELECT bytes FROM replay_storage").first()).bytes > 0);
  assert.equal((await request(env, path())).status, 404);
  env.REPLAYS.delete = remove;
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.size, 0);
  assert.equal((await query(env, "SELECT bytes FROM replay_storage").first()).bytes, 0);
});

test("a score changed during an in-flight upload cannot publish or leave an orphan", async () => {
  const env = await setup();
  let release, started;
  const ready = new Promise(r => { started = r; }), pending = new Promise(r => { release = r; });
  const put = env.REPLAYS.put.bind(env.REPLAYS);
  env.REPLAYS.put = async (...args) => { started(); await pending; await put(...args); };
  const response = upload(env);
  await ready;
  await addScore(env, 1, 200, { scoreId: scoreId(200) });
  await prunePlayer(env, { id: "sticks", generation: 1 }, 42);
  await deleteGarbage(env);
  assert.ok((await query(env, "SELECT bytes FROM replay_storage").first()).bytes > 0, "in-flight reservation is not freed early");
  release();
  assert.equal((await response).status, 409);
  assert.equal(env.REPLAYS.objects.size, 0);
  assert.equal((await query(env, "SELECT bytes FROM replay_storage").first()).bytes, 0);
});

test("per-user count/byte and global limits return Retry-After without damaging PBs", async () => {
  for (const [policy, firstExpected] of [
    [{ uploadsPerHour: 1 }, 201], [{ uploadsPerDay: 1 }, 201],
    [{ globalUploadsPerDay: 1 }, 201], [{ uploadBytesPerDay: 1 }, 429],
  ]) {
    const env = await setup(policy); await addScore(env, 2);
    assert.equal((await upload(env, 1)).status, firstExpected);
    const response = await upload(env, 2);
    assert.equal(response.status, 429); assert.ok(Number(response.headers.get("retry-after")) > 0);
    assert.equal((await query(env, "SELECT count(*) AS n FROM scores").first()).n, 2);
  }
});

test("exact fixed windows reset and rejected attempts do not grow counters", async () => {
  const env = await setup();
  await limit(env, "test", 2, 3600); await limit(env, "test", 2, 3600);
  await assert.rejects(limit(env, "test", 2, 3600), e => e.status === 429);
  assert.equal((await query(env, "SELECT count FROM limits WHERE key='test'").first()).count, 2);
  await query(env, "UPDATE limits SET expires=0 WHERE key='test'").run();
  await limit(env, "test", 2, 3600);
  assert.equal((await query(env, "SELECT count FROM limits WHERE key='test'").first()).count, 1);
});

test("edge limits reject before database or bucket work, and missing bindings fail closed", async () => {
  const env = await setup();
  env.API_RATE_LIMITER = { limit: async () => ({ success: false }) };
  env.DB.prepare = () => { throw new Error("database must not be touched"); };
  assert.equal((await request(env, "/v1/rulesets/sticks/users/42/top")).status, 429);
  delete env.API_RATE_LIMITER;
  assert.equal((await request(env, "/v1/rulesets/sticks/users/42/top")).status, 503);
});

test("recordings longer than the duration limit are rejected before storage", async () => {
  const env = await setup({ maxDurationSeconds: 2 });
  // recording() spaces frames 16.67 ms apart: 180 frames span ~3 s, 100 frames ~1.65 s.
  assert.equal((await upload(env, 1, recording(180))).status, 413);
  assert.equal(env.REPLAYS.puts, 0);
  await ok(await upload(env, 1, recording(100)), 201);
});

test("replay defaults are advertised, and no bucket does not break score/profile reads", async () => {
  const env = await setup();
  assert.equal(replayPolicy(env).retainedPerPlayer, 100);
  const policy = await ok(await request(env, "/v1/rulesets/sticks/policy"));
  assert.equal(policy.replays.enabled, true);
  assert.equal(policy.replays.maxDurationSeconds, 1800);
  delete env.REPLAYS;
  const profile = await ok(await request(env, "/v1/rulesets/sticks/users/42/top"));
  assert.equal(profile.entries[0].replay.eligible, false);
  assert.equal((await upload(env)).status, 503);
});

test("a reused score ID cannot leave a replay attached to different score data", async () => {
  const env = await setup();
  await ok(await upload(env), 201);
  await addScore(env, 1, 200);
  assert.equal((await request(env, path())).status, 404);
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.size, 0);
  await ok(await upload(env), 201);
});

test("expired uploads and late orphaned objects are recovered without deleting active files", async () => {
  const env = await setup();
  await ok(await upload(env), 201);
  await query(env, "UPDATE replays SET state='pending',lease_until=0").run();
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.size, 0);
  await ok(await upload(env), 201);
  const old = `sticks/42/1/2/${scoreId(2)}/${"b".repeat(64)}.zkr`;
  const fresh = `sticks/42/1/3/${scoreId(3)}/${"c".repeat(64)}.zkr`;
  await env.REPLAYS.put(old, recording()); await env.REPLAYS.put(fresh, recording());
  env.REPLAYS.dates.set(old, new Date(Date.now() - 2 * 86400000));
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.has(old), false);
  assert.equal(env.REPLAYS.objects.has(fresh), true);
  assert.equal(env.REPLAYS.objects.size, 2);
});

test("removing a ruleset queues its replays for cleanup", async () => {
  const env = await setup();
  await ok(await upload(env), 201); await maintainReplays(env);
  env.RULESETS = {};
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.size, 0);
});

test("normal score submission succeeds despite failed R2 cleanup, which is retried", async () => {
  const env = await setup();
  await ok(await upload(env), 201);
  const payload = await addScore(env, 2, 50);
  payload.beatmapId = 1; payload.pp = 200; payload.scoreId = scoreId(77);
  const remove = env.REPLAYS.delete.bind(env.REPLAYS);
  env.REPLAYS.delete = async () => { throw new Error("Storage unavailable"); };
  const submitted = await ok(await request(env, "/v1/rulesets/sticks/scores", "POST", JSON.stringify(payload), {
    "Content-Type": "application/json",
  }));
  assert.equal(submitted.accepted, true);
  assert.equal((await query(env, "SELECT pp FROM scores WHERE map_id=1").first()).pp, 200);
  assert.equal((await request(env, path())).status, 404);
  env.REPLAYS.delete = remove;
  await worker.scheduled({}, env);
  assert.equal(env.REPLAYS.objects.size, 0);
});

test("adding a whitelist can evict a replay, and metadata never mixes rulesets", async () => {
  const env = await setup({ retainedPerPlayer: 1 });
  await ok(await upload(env), 201);
  await addScore(env, 2, 200);
  await query(env, "UPDATE maps SET ranked=0,set_id=20 WHERE id=2").run();
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.size, 1);
  await query(env, "INSERT INTO whitelisted_sets VALUES('sticks',20)").run();
  await maintainReplays(env);
  assert.equal(env.REPLAYS.objects.size, 0);
  env.RULESETS.other = { ...env.RULESETS.sticks };
  const other = await ok(await request(env, "/v1/rulesets/other/users/42/top"));
  assert.equal(other.entries.length, 0);
  assert.equal((await request(env, path(1, scoreId(1), 42, "other"))).status, 404);
});

test("public downloads require no token and download throttling is separate from profile reads", async () => {
  const env = await setup();
  await ok(await upload(env), 201);
  assert.equal((await request(env, path(), "GET", undefined, { Authorization: "" })).status, 200);
  env.DOWNLOAD_RATE_LIMITER = { limit: async () => ({ success: false }) };
  assert.equal((await request(env, path())).status, 429);
  assert.equal((await request(env, "/v1/rulesets/sticks/users/42/top")).status, 200);
});

test("OpenAPI contract limits and DTO field names agree with the actual API", async () => {
  const spec = JSON.parse(readFileSync(new URL("../openapi.json", import.meta.url), "utf8"));
  const env = await setup();
  const policy = await ok(await request(env, "/v1/rulesets/sticks/policy"));
  const documented = spec.paths["/v1/rulesets/{ruleset}/policy"].get.responses["200"].content["application/json"].example;
  assert.deepEqual(policy.replays, documented.replays);
  const profile = await ok(await request(env, "/v1/rulesets/sticks/users/42/top"));
  assert.deepEqual(Object.keys(profile.entries[0]).sort(), Object.keys(spec.components.schemas.Play.properties).sort());
});

test("same-ID score mutations during validation cannot package stale score metadata", async () => {
  const env = await setup(), prepare = env.DB.prepare.bind(env.DB);
  env.DB.prepare = sql => {
    const prepared = prepare(sql);
    if (!sql.startsWith("INSERT INTO replays")) return prepared;
    return { bind: (...args) => {
      const bound = prepared.bind(...args);
      return { ...bound, first: async () => {
        await addScore(env, 1, 200);
        return bound.first();
      } };
    } };
  };
  assert.equal((await upload(env)).status, 409);
  assert.equal(env.REPLAYS.objects.size, 0);
  assert.equal((await query(env, "SELECT bytes FROM replay_storage").first()).bytes, 0);
});

test("maintenance stays inside the Free plan D1 query allowance with a full owner batch", async () => {
  const env = await setup();
  for (let user = 50; user < 59; user++) {
    await query(env, "INSERT INTO users VALUES(?,?)", user, `Player ${user}`).run();
    await addScore(env, user, 100, { userId: user });
  }
  const prepare = env.DB.prepare.bind(env.DB);
  let queries = 0;
  env.DB.prepare = sql => { queries++; return prepare(sql); };
  await worker.scheduled({}, env);
  assert.ok(queries <= 50, `maintenance used ${queries} queries`);
});
