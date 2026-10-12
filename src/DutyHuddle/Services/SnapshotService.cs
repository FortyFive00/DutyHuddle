using Dalamud.Plugin.Services;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Serialization;
using DutyHuddle.Domain.Time;
using DutyHuddle.GameAdapters;

namespace DutyHuddle.Services;

/// <summary>
/// Reads the local player's state on the framework thread, at most every few seconds, and publishes a
/// new snapshot to the store only when something actually changed.
/// </summary>
public sealed class SnapshotService
{
    private static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SlowInterval = TimeSpan.FromSeconds(30);

    private readonly IPlayerState player;
    private readonly DutyCatalog catalog;
    private readonly PartyScanner partyScanner;
    private readonly RouletteReader rouletteReader;
    private readonly WondrousTailsReader wtReader;
    private readonly PartyStateStore store;
    private readonly IPluginLog log;

    private DateTime lastRead = DateTime.MinValue;
    private bool refreshRequested = true;
    private string? lastSignature;

    public SnapshotService(
        IPlayerState player,
        DutyCatalog catalog,
        PartyScanner partyScanner,
        RouletteReader rouletteReader,
        WondrousTailsReader wtReader,
        PartyStateStore store,
        IPluginLog log)
    {
        this.player = player;
        this.catalog = catalog;
        this.partyScanner = partyScanner;
        this.rouletteReader = rouletteReader;
        this.wtReader = wtReader;
        this.store = store;
        this.log = log;
    }

    /// <summary>Raised (on the framework thread) when the local snapshot content changed.</summary>
    public event Action<MemberSnapshot>? LocalChanged;

    public void RequestRefresh() => refreshRequested = true;

    /// <summary>Call from Framework.Update.</summary>
    public void Tick(DateTime nowUtc, bool windowOpen)
    {
        var interval = windowOpen ? FastInterval : SlowInterval;
        if (!refreshRequested && nowUtc - lastRead < interval)
            return;

        refreshRequested = false;
        lastRead = nowUtc;

        if (!player.IsLoaded)
            return;

        try
        {
            store.SetMembers(partyScanner.Read());
            ReadLocal(nowUtc);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to read the local state.");
        }
    }

    private void ReadLocal(DateTime nowUtc)
    {
        if (partyScanner.LocalId is not { } id)
            return;

        var snapshot = new MemberSnapshot(
            id,
            nowUtc,
            GameClock.DailyEpoch(nowUtc),
            rouletteReader.Read(catalog),
            wtReader.Read(),
            SnapshotSource.Local);

        // Compare content only: the capture time changes on every read.
        var signature = Convert.ToBase64String(SnapshotCodec.Encode(snapshot with { CapturedUtc = DateTime.UnixEpoch }));
        if (signature == lastSignature && store.Local?.Member == id)
            return;

        lastSignature = signature;
        store.SetLocal(snapshot);
        LocalChanged?.Invoke(snapshot);
    }
}
