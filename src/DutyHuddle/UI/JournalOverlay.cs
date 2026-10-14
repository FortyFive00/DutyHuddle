using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Solver;
using DutyHuddle.Services;

namespace DutyHuddle.UI;

/// <summary>
/// A small DutyHuddle panel on the game's own Wondrous Tails journal (the WeeklyBingo addon):
/// the odds for 1, 2 and 3 lines and a shortcut to the plugin's Wondrous Tails page.
/// </summary>
public sealed class JournalOverlay
{
    private const string AddonName = "WeeklyBingo";

    // Free space on the left page, under the objective description, as fractions of the addon size.
    private const float AnchorX = 0.055f;
    private const float AnchorBottom = 0.955f;
    private const float PanelWidth = 0.40f;

    private readonly IGameGui gameGui;
    private readonly AnalysisService analysis;
    private readonly PluginConfig config;
    private readonly SolverCache solvers;
    private readonly Widgets ui;
    private readonly Action openJournalPage;

    public JournalOverlay(IGameGui gameGui, AnalysisService analysis, PluginConfig config, SolverCache solvers, Widgets ui, Action openJournalPage)
    {
        this.gameGui = gameGui;
        this.analysis = analysis;
        this.config = config;
        this.solvers = solvers;
        this.ui = ui;
        this.openJournalPage = openJournalPage;
    }

    /// <summary>True while the game's journal is on screen; the journal is then read more often.</summary>
    public bool IsJournalOpen { get; private set; }

    public void Draw()
    {
        var addon = gameGui.GetAddonByName(AddonName);
        IsJournalOpen = !addon.IsNull && addon.IsVisible;
        if (!IsJournalOpen || !config.ShowJournalOverlay)
            return;

        Theme.Scale = ui.Assets.Scale;
        using var style = Theme.PushWindowStyle()
            .Var(ImGuiStyleVar.WindowPadding, Theme.S(10, 8))
            .Var(ImGuiStyleVar.ItemSpacing, Theme.S(6, 5))
            .Color(ImGuiCol.Border, Theme.Alpha(Theme.Gold, 0.45f));

        var origin = new Vector2(addon.X, addon.Y);
        var size = addon.ScaledSize;
        ImGui.SetNextWindowPos(origin + new Vector2(size.X * AnchorX, size.Y * AnchorBottom), ImGuiCond.Always, new Vector2(0, 1));
        ImGui.SetNextWindowSizeConstraints(new Vector2(size.X * PanelWidth, 0), new Vector2(size.X * PanelWidth, float.MaxValue));

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
                                       | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.AlwaysAutoResize;
        if (ImGui.Begin("##DutyHuddleJournalOverlay", flags))
            DrawPanel();
        ImGui.End();
    }

    /// <summary>The panel's content, drawn inside the overlay window (or a preview).</summary>
    public void DrawPanel()
    {
        var view = analysis.Current;
        var journal = view.Journals.FirstOrDefault(j => j.IsLocal);
        var solver = solvers.TryGet(config.AdvisorGoal);
        var result = journal?.Board is { } current && solver is not null
            ? StickerAdvisor.Advise(current, solver, config.SkippedFor(journal.Member, current), view.ComputedUtc)
            : null;

        // Top line: the advice, and the shortcut to the plugin on the right.
        var startY = ImGui.GetCursorPosY();
        var buttonWidth = ui.ButtonWidth(FontAwesomeIcon.BookOpen, "Open");
        ImGui.SetCursorPosY(startY + (ImGui.GetFrameHeight() - ImGui.GetTextLineHeight()) / 2);
        var status = journal?.Board is null ? "Reading your journal..." : result is null ? "Calculating odds..." : result.Headline;
        Widgets.Text(status, result is null ? Theme.TextMuted : Theme.Gold);
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - buttonWidth);
        ImGui.SetCursorPosY(startY);
        if (ui.Button("overlayOpen", FontAwesomeIcon.BookOpen, "Open", primary: true))
            openJournalPage();
        Widgets.Tooltip("Open Wondrous Tails in DutyHuddle");

        if (result is null || journal?.Board is not { } board)
            return;

        // Bottom line: the odds of playing on optimally, label then value.
        if (result.Evaluation is { } evaluation)
        {
            var odds = evaluation.Optimal;
            var start = ImGui.GetCursorPosX();
            var column = ImGui.GetContentRegionAvail().X / 3;
            Odds("1+", odds.AtLeastOne, 1);
            ImGui.SameLine(start + column);
            Odds("2+", odds.AtLeastTwo, 1);
            ImGui.SameLine(start + column * 2);
            Odds("3", odds.Three, 2);
        }
        else
        {
            var lines = LineTable.CountLines(board.StickerMask);
            Widgets.Text(lines == 1 ? "Journal complete: 1 line" : $"Journal complete: {lines} lines", Theme.TextMuted);
        }
    }

    /// <summary>One odds column: "2+ lines 15.3%".</summary>
    private static void Odds(string lines, double value, int decimals)
    {
        Widgets.Text(lines == "1+" ? "1+ line" : $"{lines} lines", Theme.TextFaint);
        ImGui.SameLine(0, Theme.S(5));
        Widgets.Text(StickerAdvisor.Percent(value, decimals), Theme.Text);
    }
}
