using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Duty;
using DutyHuddle.GameAdapters;
using DutyHuddle.Ipc;
using DutyHuddle.Services;
using DutyHuddle.Sync;
using DutyHuddle.UI;
using DutyHuddle.UI.Pages;

namespace DutyHuddle;

/// <summary>Composition root: builds every service explicitly and wires the game events.</summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/dutyhuddle";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly PluginConfig config;
    private readonly WindowSystem windows = new("DutyHuddle");
    private readonly PartyStateStore store;
    private readonly SnapshotService snapshots;
    private readonly SyncService sync;
    private readonly ChatCodeService chatCodes;
    private readonly DutyEventsListener dutyEvents;
    private readonly AnalysisService analysis;
    private readonly HistoryService history;
    private readonly NotificationService notificationService;
    private readonly IpcProvider ipc;
    private readonly DalamudUiAssets assets;
    private readonly MainWindow mainWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly IChatGui chat;
    private readonly Func<IEnumerable<string>> describeMentor;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IFramework framework,
        IClientState clientState,
        IPlayerState playerState,
        IPartyList partyList,
        IDataManager dataManager,
        IDutyState dutyState,
        IAddonLifecycle addonLifecycle,
        ITextureProvider textures,
        INotificationManager notifications,
        IChatGui chat,
        IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.chat = chat;

        config = pluginInterface.GetPluginConfig() as PluginConfig ?? new PluginConfig();
        config.PruneSkipped(DateTime.UtcNow);
        if (config.Migrate())
            SaveConfig();

        var data = new GameDataRepository(new DalamudSheetSource(dataManager));
        var catalog = data.Load();
        var resolver = new WtObjectiveResolver(catalog);
        log.Information($"DutyHuddle loaded {catalog.Duties.Count} duties and {catalog.Roulettes.Count} daily roulettes.");

        store = new PartyStateStore();
        var partyScanner = new PartyScanner(playerState, partyList);
        var rouletteReader = new RouletteReader(playerState);
        describeMentor = () => rouletteReader.DescribeMentor(catalog);
        snapshots = new SnapshotService(playerState, catalog, partyScanner, rouletteReader, new WondrousTailsReader(), store, log);
        history = new HistoryService(pluginInterface.ConfigDirectory.FullName, log);
        snapshots.LocalChanged += history.Record;

        var version = typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        sync = new SyncService(config, store, log, $"DutyHuddle/{version}");
        chatCodes = new ChatCodeService(chat, store, catalog, config);
        dutyEvents = new DutyEventsListener(clientState, dutyState, addonLifecycle, snapshots, config, SaveConfig);

        analysis = new AnalysisService(store, config, catalog, resolver);
        notificationService = new NotificationService(notifications, config);
        analysis.Updated += notificationService.OnViewUpdated;

        ipc = new IpcProvider(pluginInterface, store, analysis);
        store.Changed += OnStoreChanged;

        assets = new DalamudUiAssets(pluginInterface.UiBuilder, textures);
        var ui = new Widgets(assets);
        var dutyFinder = new DutyFinderLauncher(framework);
        var sharing = new SharingFacade(sync, chatCodes, store);
        settingsWindow = new SettingsWindow(ui, config, SaveConfig, catalog);
        var view = new MainView(
            ui,
            config,
            SaveConfig,
            sharing,
            new RoulettePage(ui, config, SaveConfig, dutyFinder, history),
            new WondrousTailsPage(ui, config, SaveConfig, catalog, data.LoadExpansionNames(), dutyFinder, new SolverCache(log)),
            new SharingPage(ui, config, SaveConfig, sharing),
            () => settingsWindow.IsOpen = true);
        mainWindow = new MainWindow(analysis, view, assets);
        windows.AddWindow(mainWindow);
        windows.AddWindow(settingsWindow);

        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        pluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;
        commands.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open DutyHuddle. Use \"/dutyhuddle settings\" for the settings window.",
        });
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        commands.RemoveHandler(Command);
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        pluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        pluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        windows.RemoveAllWindows();

        store.Changed -= OnStoreChanged;
        analysis.Updated -= notificationService.OnViewUpdated;
        snapshots.LocalChanged -= history.Record;
        ipc.Dispose();
        dutyEvents.Dispose();
        chatCodes.Dispose();
        sync.Dispose();
        assets.Dispose();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        snapshots.Tick(now, mainWindow.IsOpen);
        sync.Tick(now, mainWindow.IsOpen);
    }

    /// <summary>The store also changes from network threads; IPC subscribers are always called on the game thread.</summary>
    private void OnStoreChanged() => framework.RunOnFrameworkThread(ipc.NotifyStateChanged);

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("settings", StringComparison.OrdinalIgnoreCase))
            ToggleSettings();
        else if (arguments.Trim().Equals("debug", StringComparison.OrdinalIgnoreCase))
            framework.RunOnFrameworkThread(() =>
            {
                foreach (var line in describeMentor())
                    chat.Print(line, "DutyHuddle");
            });
        else
            ToggleMain();
    }

    private void ToggleMain()
    {
        mainWindow.Toggle();
        if (mainWindow.IsOpen)
        {
            snapshots.RequestRefresh();
            sync.RequestPoll();
        }
    }

    private void ToggleSettings() => settingsWindow.Toggle();

    private void SaveConfig() => pluginInterface.SavePluginConfig(config);
}
