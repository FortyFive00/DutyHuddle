using Dalamud.Plugin.Services;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Security;
using DutyHuddle.Domain.Serialization;
using DutyHuddle.GameAdapters;
using DutyHuddle.Services;

namespace DutyHuddle.Sync;

public enum SharingState
{
    Off,
    NoServer,
    Solo,
    Connecting,
    Connected,
    Unreachable,
}

/// <summary>
/// Publishes the local snapshot to the party room when it changes and pulls the other members'
/// snapshots. Runs only when the player switched sharing on.
/// </summary>
public sealed class SyncService : IDisposable
{
    private static readonly TimeSpan PollWhileOpen = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInBackground = TimeSpan.FromMinutes(5);
    // Keeps the slot alive on the server, which deletes it 2 hours after the last update: stay well under that.
    private static readonly TimeSpan Republish = TimeSpan.FromMinutes(30);

    private readonly PluginConfig config;
    private readonly PartyStateStore store;
    private readonly IPluginLog log;
    private readonly string userAgent;
    private readonly CancellationTokenSource shutdown = new();

    private RelayTransport? transport;
    private Room? room;
    private DateTime lastPoll = DateTime.MinValue;
    private DateTime lastPublish = DateTime.MinValue;
    private MemberSnapshot? lastPublished;
    private string? etag;
    private int busy;

    public SyncService(PluginConfig config, PartyStateStore store, IPluginLog log, string userAgent)
    {
        this.config = config;
        this.store = store;
        this.log = log;
        this.userAgent = userAgent;
    }

    public SharingState State { get; private set; } = SharingState.Off;

    public string Status => State switch
    {
        SharingState.Off => "Sharing off",
        SharingState.NoServer => "No sharing server",
        SharingState.Solo => "Sharing on · not in a party",
        SharingState.Connecting => "Sharing on · connecting",
        SharingState.Connected => "Sharing on",
        _ => "Sharing server unreachable",
    };

    public DateTime? LastSuccessUtc { get; private set; }

    public bool IsActive => State == SharingState.Connected;

    public bool HasServer => TryGetUri(out _);

    public void RequestPoll() => lastPoll = DateTime.MinValue;

    /// <summary>Call from Framework.Update. Network work runs in the background.</summary>
    public void Tick(DateTime nowUtc, bool windowOpen)
    {
        if (!config.ShareWithParty)
        {
            room = null;
            State = SharingState.Off;
            return;
        }

        if (!TryGetTransport(out var relay))
        {
            room = null;
            State = SharingState.NoServer;
            return;
        }

        var members = store.Members;
        var local = store.Local;
        if (members.Count < 2 || local is null)
        {
            room = null;
            State = SharingState.Solo;
            return;
        }

        if (State is SharingState.Off or SharingState.NoServer or SharingState.Solo)
            State = SharingState.Connecting;

        // The store hands out the same list until the party changes, so the key is derived once per party.
        if (room is null || !ReferenceEquals(room.Members, members) || room.Mode != config.KeyMode)
        {
            var current = BuildRoom(members);
            if (room is null || room.Id != current.Id)
            {
                etag = null;
                lastPublished = null;
                lastPoll = DateTime.MinValue;
            }

            room = current;
        }

        if (Volatile.Read(ref busy) != 0)
            return;

        var mustPublish = !ReferenceEquals(lastPublished, local) || nowUtc - lastPublish > Republish;
        var mustPoll = nowUtc - lastPoll > (windowOpen ? PollWhileOpen : PollInBackground);
        if (!mustPublish && !mustPoll)
            return;

        Interlocked.Exchange(ref busy, 1);
        var activeRoom = room;
        _ = Task.Run(async () =>
        {
            try
            {
                if (mustPublish)
                {
                    var slot = activeRoom.SlotFor(local.Member);
                    var blob = RoomCrypto.Seal(activeRoom.Key, slot, SnapshotCodec.Encode(local));
                    await relay.PutAsync(activeRoom.Id, slot, blob, shutdown.Token).ConfigureAwait(false);
                    lastPublished = local;
                    lastPublish = nowUtc;
                }

                if (mustPublish || mustPoll)
                {
                    lastPoll = nowUtc;
                    await PollAsync(relay, activeRoom).ConfigureAwait(false);
                }

                LastSuccessUtc = DateTime.UtcNow;
                State = SharingState.Connected;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                State = SharingState.Unreachable;
                log.Warning(ex, "Relay sync failed.");
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        });
    }

    public void Dispose()
    {
        shutdown.Cancel();
        transport?.Dispose();
        shutdown.Dispose();
    }

    private async Task PollAsync(RelayTransport relay, Room activeRoom)
    {
        var fetched = await relay.GetAsync(activeRoom.Id, etag, shutdown.Token).ConfigureAwait(false);
        if (fetched is null)
            return;

        etag = fetched.ETag;
        var bySlot = fetched.Slots.ToDictionary(s => s.Slot, StringComparer.Ordinal);
        foreach (var member in activeRoom.Members.Where(m => !m.IsLocal))
        {
            var slot = activeRoom.SlotFor(member.Id);
            if (!bySlot.TryGetValue(slot, out var entry))
                continue;

            if (RoomCrypto.TryOpen(activeRoom.Key, slot, entry.Blob, out var plaintext)
                && SnapshotCodec.TryDecode(plaintext, member.Id, SnapshotSource.Relay, out var snapshot))
                store.UpdateRemote(snapshot!);
        }
    }

    private Room BuildRoom(IReadOnlyList<PartyMemberInfo> members)
    {
        var useContentIds = config.KeyMode == RoomKeyMode.ContentIds && members.All(m => m.ContentId != 0);
        var key = useContentIds
            ? RoomCrypto.DeriveFromContentIds(members.Select(m => m.ContentId))
            : RoomCrypto.DeriveFromMembers(members.Select(m => m.Id));
        return new Room(RoomCrypto.RoomId(key), key, members, config.KeyMode);
    }

    private bool TryGetUri(out Uri uri)
        => Uri.TryCreate(SyncDefaults.Effective(config.RelayUrl).TrimEnd('/') + "/", UriKind.Absolute, out uri!)
           && (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);

    private bool TryGetTransport(out RelayTransport relay)
    {
        relay = null!;
        if (!TryGetUri(out var uri))
        {
            transport?.Dispose();
            transport = null;
            return false;
        }

        if (transport is null || transport.BaseAddress != uri)
        {
            transport?.Dispose();
            transport = new RelayTransport(uri, userAgent);
        }

        relay = transport;
        return true;
    }

    private sealed record Room(string Id, byte[] Key, IReadOnlyList<PartyMemberInfo> Members, RoomKeyMode Mode)
    {
        public string SlotFor(MemberId member) => RoomCrypto.SlotId(Key, member);
    }
}
