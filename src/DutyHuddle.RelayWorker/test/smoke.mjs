// Contract test for any DutyHuddle relay (Cloudflare Worker or ASP.NET).
// Usage: node test/smoke.mjs [base url]   (default http://127.0.0.1:8787)
import assert from "node:assert/strict";
import { randomBytes } from "node:crypto";

const base = (process.argv[2] ?? "http://127.0.0.1:8787").replace(/\/$/, "");
const hex = () => randomBytes(16).toString("hex");
const room = hex();
const slotA = hex();
const slotB = hex();

async function put(roomId, slotId, bytes) {
  return fetch(`${base}/v1/rooms/${roomId}/slots/${slotId}`, {
    method: "PUT",
    headers: { "content-type": "application/octet-stream" },
    body: bytes,
  });
}

const tests = [];
const test = (name, fn) => tests.push({ name, fn });

test("health", async () => {
  const response = await fetch(`${base}/v1/health`);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { status: "ok" });
});

test("unknown room is 404", async () => {
  assert.equal((await fetch(`${base}/v1/rooms/${hex()}`)).status, 404);
});

test("put then get returns the blobs", async () => {
  const a = new Uint8Array([1, 2, 3, 4, 5]);
  const b = new Uint8Array([9, 8, 7]);
  assert.equal((await put(room, slotA, a)).status, 204);
  assert.equal((await put(room, slotB, b)).status, 204);

  const response = await fetch(`${base}/v1/rooms/${room}`);
  assert.equal(response.status, 200);
  const body = await response.json();
  assert.equal(body.slots.length, 2);
  const byId = Object.fromEntries(body.slots.map((s) => [s.slot, s]));
  assert.deepEqual([...Buffer.from(byId[slotA].blob, "base64")], [...a]);
  assert.deepEqual([...Buffer.from(byId[slotB].blob, "base64")], [...b]);
  assert.ok(Math.abs(byId[slotA].updatedUnix - Date.now() / 1000) < 120);
});

test("etag gives 304 until the room changes", async () => {
  const first = await fetch(`${base}/v1/rooms/${room}`);
  const etag = first.headers.get("etag");
  assert.ok(etag);
  assert.equal((await fetch(`${base}/v1/rooms/${room}`, { headers: { "if-none-match": etag } })).status, 304);
  assert.equal((await put(room, slotA, new Uint8Array([42]))).status, 204);
  assert.equal((await fetch(`${base}/v1/rooms/${room}`, { headers: { "if-none-match": etag } })).status, 200);
});

test("invalid ids are rejected", async () => {
  assert.equal((await put("not-a-room", slotA, new Uint8Array([1]))).status, 400);
  assert.equal((await put(room, "UPPERCASE00000000000000000000000", new Uint8Array([1]))).status, 400);
});

test("empty and oversized blobs are rejected", async () => {
  assert.equal((await put(room, slotA, new Uint8Array(0))).status, 400);
  assert.equal((await put(room, slotA, new Uint8Array(513))).status, 413);
});

test("a room holds at most 16 members", async () => {
  const crowded = hex();
  for (let i = 0; i < 16; i++) {
    assert.equal((await put(crowded, hex(), new Uint8Array([i]))).status, 204);
  }
  assert.equal((await put(crowded, hex(), new Uint8Array([99]))).status, 409);
});

let failed = 0;
for (const { name, fn } of tests) {
  try {
    await fn();
    console.log(`ok   ${name}`);
  } catch (error) {
    failed++;
    console.log(`FAIL ${name}: ${error.message}`);
  }
}

console.log(failed === 0 ? `All ${tests.length} checks passed against ${base}` : `${failed} check(s) failed`);
process.exit(failed === 0 ? 0 : 1);
