using System.Collections.Concurrent;

namespace DutyHuddle.Relay;

public sealed class RelayOptions
{
    public TimeSpan SlotTimeToLive { get; set; } = TimeSpan.FromHours(2);
    public int MaxBlobBytes { get; set; } = 512;
    public int MaxSlotsPerRoom { get; set; } = 16;
    public int MaxRooms { get; set; } = 200_000;
    public int RequestsPerMinutePerClient { get; set; } = 120;
}

public enum PutResult
{
    Stored,
    RoomFull,
    StoreFull,
}

public sealed record SlotView(string Slot, string Blob, long UpdatedUnix);

public sealed record RoomView(long Version, IReadOnlyList<SlotView> Slots);

/// <summary>
/// In-memory store of encrypted blobs. It never sees plaintext, names or IDs: rooms and slots are
/// opaque hashes chosen by the clients. Nothing is written to disk; a restart forgets everything.
/// </summary>
public sealed class RoomStore
{
    private readonly ConcurrentDictionary<string, Room> rooms = new(StringComparer.Ordinal);
    private readonly RelayOptions options;
    private readonly TimeProvider clock;
    private long versionCounter;

    public RoomStore(RelayOptions options, TimeProvider clock)
    {
        this.options = options;
        this.clock = clock;
    }

    public int RoomCount => rooms.Count;

    public PutResult Put(string roomId, string slotId, byte[] blob)
    {
        if (!rooms.ContainsKey(roomId) && rooms.Count >= options.MaxRooms)
        {
            Prune();
            if (rooms.Count >= options.MaxRooms)
                return PutResult.StoreFull;
        }

        var room = rooms.GetOrAdd(roomId, _ => new Room());
        var now = clock.GetUtcNow();
        lock (room)
        {
            RemoveExpired(room, now);
            if (!room.Slots.ContainsKey(slotId) && room.Slots.Count >= options.MaxSlotsPerRoom)
                return PutResult.RoomFull;

            room.Slots[slotId] = new Slot(blob, now);
            room.Version = Interlocked.Increment(ref versionCounter);
        }

        return PutResult.Stored;
    }

    public RoomView? Get(string roomId)
    {
        if (!rooms.TryGetValue(roomId, out var room))
            return null;

        lock (room)
        {
            RemoveExpired(room, clock.GetUtcNow());
            if (room.Slots.Count == 0)
                return null;

            var slots = room.Slots
                .Select(kv => new SlotView(kv.Key, Convert.ToBase64String(kv.Value.Blob), kv.Value.Updated.ToUnixTimeSeconds()))
                .OrderBy(s => s.Slot, StringComparer.Ordinal)
                .ToList();
            return new RoomView(room.Version, slots);
        }
    }

    /// <summary>Drops expired slots and empty rooms. Called periodically and when the store is full.</summary>
    public int Prune()
    {
        var now = clock.GetUtcNow();
        var removed = 0;
        foreach (var (id, room) in rooms)
        {
            lock (room)
            {
                RemoveExpired(room, now);
                if (room.Slots.Count > 0)
                    continue;
            }

            if (rooms.TryRemove(id, out _))
                removed++;
        }

        return removed;
    }

    private void RemoveExpired(Room room, DateTimeOffset now)
    {
        foreach (var (slotId, slot) in room.Slots.ToList())
            if (now - slot.Updated > options.SlotTimeToLive)
                room.Slots.Remove(slotId);
    }

    private sealed class Room
    {
        public Dictionary<string, Slot> Slots { get; } = new(StringComparer.Ordinal);
        public long Version { get; set; }
    }

    private sealed record Slot(byte[] Blob, DateTimeOffset Updated);
}
