using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.DutyState;
using Dalamud.Plugin.Services;
using DutyHuddle.Configuration;
using DutyHuddle.Services;

namespace DutyHuddle.GameAdapters;

/// <summary>
/// Turns game events into refresh requests, and measures how long each duty takes to clear so
/// the time estimates become the player's own.
/// </summary>
public sealed class DutyEventsListener : IDisposable
{
    private const string WondrousTailsAddon = "WeeklyBingo";
    private static readonly TimeSpan MinPlausible = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxPlausible = TimeSpan.FromHours(3);

    private readonly IClientState clientState;
    private readonly IDutyState dutyState;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly SnapshotService snapshots;
    private readonly PluginConfig config;
    private readonly Action saveConfig;

    private uint startedDuty;
    private DateTime startedAt;

    public DutyEventsListener(
        IClientState clientState,
        IDutyState dutyState,
        IAddonLifecycle addonLifecycle,
        SnapshotService snapshots,
        PluginConfig config,
        Action saveConfig)
    {
        this.clientState = clientState;
        this.dutyState = dutyState;
        this.addonLifecycle = addonLifecycle;
        this.snapshots = snapshots;
        this.config = config;
        this.saveConfig = saveConfig;

        clientState.Login += OnLogin;
        clientState.TerritoryChanged += OnTerritoryChanged;
        dutyState.DutyStarted += OnDutyStarted;
        dutyState.DutyCompleted += OnDutyCompleted;
        addonLifecycle.RegisterListener(AddonEvent.PostSetup, WondrousTailsAddon, OnJournalEvent);
        addonLifecycle.RegisterListener(AddonEvent.PostRefresh, WondrousTailsAddon, OnJournalEvent);
    }

    public void Dispose()
    {
        clientState.Login -= OnLogin;
        clientState.TerritoryChanged -= OnTerritoryChanged;
        dutyState.DutyStarted -= OnDutyStarted;
        dutyState.DutyCompleted -= OnDutyCompleted;
        addonLifecycle.UnregisterListener(AddonEvent.PostSetup, WondrousTailsAddon, OnJournalEvent);
        addonLifecycle.UnregisterListener(AddonEvent.PostRefresh, WondrousTailsAddon, OnJournalEvent);
    }

    private void OnLogin() => snapshots.RequestRefresh();

    private void OnTerritoryChanged(uint territory) => snapshots.RequestRefresh();

    private void OnJournalEvent(AddonEvent type, AddonArgs args) => snapshots.RequestRefresh();

    private void OnDutyStarted(IDutyStateEventArgs args)
    {
        startedDuty = args.ContentFinderCondition.RowId;
        startedAt = DateTime.UtcNow;
    }

    private void OnDutyCompleted(IDutyStateEventArgs args)
    {
        snapshots.RequestRefresh();

        var duty = args.ContentFinderCondition.RowId;
        if (duty == 0 || duty != startedDuty)
            return;

        var elapsed = DateTime.UtcNow - startedAt;
        startedDuty = 0;
        if (elapsed < MinPlausible || elapsed > MaxPlausible)
            return;

        config.AddMeasured(duty, elapsed.TotalMinutes);
        saveConfig();
    }
}
