// DutyHuddle sharing server for Cloudflare Workers.
//
// Same contract as src/DutyHuddle.Relay (ASP.NET):
//   PUT /v1/rooms/{room}/slots/{slot}   raw encrypted blob, at most 512 bytes  -> 204
//   GET /v1/rooms/{room}                {version, slots:[{slot, blob, updatedUnix}]} with ETag -> 200 / 304 / 404
//   GET /v1/health                      {status:"ok"}
// Rooms and slots are 32 hex characters chosen by the clients (hashes). The server only ever stores
// ciphertext: it cannot read names, worlds, IDs or game data. Each room is one Durable Object; slots
// expire 2 hours after their last update (clients republish every 30 minutes while in a party)
// and an alarm deletes empty rooms.

const HEX_ID = /^[0-9a-f]{32}$/;
const MAX_BLOB_BYTES = 512;
const MAX_SLOTS_PER_ROOM = 16;
const SLOT_TTL_MS = 2 * 60 * 60 * 1000;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const parts = url.pathname.split("/").filter(Boolean);

    if (request.method === "GET" && url.pathname === "/v1/health") {
      return json({ status: "ok" });
    }

    if (parts[0] !== "v1" || parts[1] !== "rooms" || parts.length < 3) {
      return text("Not found", 404);
    }

    const roomId = parts[2];
    if (!HEX_ID.test(roomId)) {
      return text("Bad request", 400);
    }

    const room = env.ROOMS.get(env.ROOMS.idFromName(roomId));

    if (request.method === "GET" && parts.length === 3) {
      return room.fetch("https://room/get", {
        headers: { "if-none-match": request.headers.get("if-none-match") ?? "" },
      });
    }

    if (request.method === "PUT" && parts.length === 5 && parts[3] === "slots") {
      const slotId = parts[4];
      if (!HEX_ID.test(slotId)) {
        return text("Bad request", 400);
      }

      if (Number(request.headers.get("content-length") ?? "0") > MAX_BLOB_BYTES) {
        return text("Payload too large", 413);
      }

      const blob = await readLimited(request, MAX_BLOB_BYTES);
      if (blob === null) {
        return text("Payload too large", 413);
      }
      if (blob.byteLength === 0) {
        return text("Bad request", 400);
      }

      return room.fetch(`https://room/put/${slotId}`, { method: "PUT", body: blob });
    }

    return text("Method not allowed", 405);
  },
};

/** One party room: its encrypted slots in the Durable Object's SQLite storage. */
export class Room {
  constructor(ctx) {
    this.ctx = ctx;
    this.sql = ctx.storage.sql;
    this.ready = false;
  }

  async fetch(request) {
    this.ensureSchema();
    const now = Date.now();
    this.sql.exec("DELETE FROM slots WHERE updated < ?", now - SLOT_TTL_MS);

    const url = new URL(request.url);
    if (request.method === "PUT" && url.pathname.startsWith("/put/")) {
      return this.put(url.pathname.slice("/put/".length), await request.arrayBuffer(), now);
    }

    return this.get(request.headers.get("if-none-match") ?? "");
  }

  async put(slotId, blob, now) {
    const exists = this.sql.exec("SELECT 1 FROM slots WHERE slot = ?", slotId).toArray().length > 0;
    const count = this.sql.exec("SELECT COUNT(*) AS n FROM slots").one().n;
    if (!exists && count >= MAX_SLOTS_PER_ROOM) {
      return text("Room full", 409);
    }

    this.sql.exec("INSERT OR REPLACE INTO slots (slot, blob, updated) VALUES (?, ?, ?)", slotId, blob, now);
    this.sql.exec("UPDATE meta SET value = value + 1 WHERE key = 'version'");
    await this.ctx.storage.setAlarm(now + SLOT_TTL_MS + 60_000);
    return new Response(null, { status: 204 });
  }

  get(ifNoneMatch) {
    const rows = this.sql.exec("SELECT slot, blob, updated FROM slots ORDER BY slot").toArray();
    if (rows.length === 0) {
      return text("Not found", 404);
    }

    const version = this.sql.exec("SELECT value FROM meta WHERE key = 'version'").one().value;
    const etag = `"${version}"`;
    // Cloudflare turns the ETag into a weak one (W/"n") when it compresses the response; compare without it.
    if (ifNoneMatch.split(",").map((tag) => tag.trim().replace(/^W\//, "")).includes(etag)) {
      return new Response(null, { status: 304, headers: { etag } });
    }

    return json(
      {
        version,
        slots: rows.map((row) => ({
          slot: row.slot,
          blob: base64(row.blob),
          updatedUnix: Math.floor(row.updated / 1000),
        })),
      },
      200,
      { etag, "cache-control": "no-store" },
    );
  }

  async alarm() {
    this.ensureSchema();
    const now = Date.now();
    this.sql.exec("DELETE FROM slots WHERE updated < ?", now - SLOT_TTL_MS);
    const latest = this.sql.exec("SELECT MAX(updated) AS latest FROM slots").one().latest;
    if (latest === null) {
      // Nothing left: remove the room entirely.
      await this.ctx.storage.deleteAll();
      this.ready = false;
      return;
    }

    await this.ctx.storage.setAlarm(latest + SLOT_TTL_MS + 60_000);
  }

  ensureSchema() {
    if (this.ready) {
      return;
    }

    this.sql.exec("CREATE TABLE IF NOT EXISTS slots (slot TEXT PRIMARY KEY, blob BLOB NOT NULL, updated INTEGER NOT NULL)");
    this.sql.exec("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value INTEGER NOT NULL)");
    this.sql.exec("INSERT OR IGNORE INTO meta (key, value) VALUES ('version', 0)");
    this.ready = true;
  }
}

/** Reads the body, returning null as soon as it exceeds the limit. */
async function readLimited(request, limit) {
  if (!request.body) {
    return new ArrayBuffer(0);
  }

  const reader = request.body.getReader();
  const chunks = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) {
      break;
    }

    total += value.byteLength;
    if (total > limit) {
      await reader.cancel();
      return null;
    }

    chunks.push(value);
  }

  const result = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    result.set(chunk, offset);
    offset += chunk.byteLength;
  }

  return result.buffer;
}

function base64(buffer) {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary);
}

function json(body, status = 200, headers = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", ...headers },
  });
}

function text(body, status) {
  return new Response(body, { status, headers: { "content-type": "text/plain; charset=utf-8" } });
}
