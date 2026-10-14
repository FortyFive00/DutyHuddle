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
    private const float PanelWidth = 0.37f;

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
        var width = ImGui.GetContentRegionAvail().X;
        Widgets.Logo(Theme.S(16));
        ImGui.SameLine(0, Theme.S(6));
        Widgets.Text("DutyHuddle", Theme.Gold);

        var view = analysis.Current;
        var journal = view.Journals.FirstOrDefault(j => j.IsLocal);
        var solver = solvers.TryGet(config.AdvisorGoal);
        if (journal?.Board is not { } board)
        {
            Widgets.Text("Reading your journal...", Theme.TextMuted);
        }
        else if (solver is null)
        {
            Widgets.Text("Calculating odds...", Theme.TextMuted);
        }
        else
        {
            var result = StickerAdvisor.Advise(board, solver, config.SkippedFor(journal.Member, board), view.ComputedUtc);
            ImGui.SameLine();
            var headlineWidth = ImGui.CalcTextSize(result.Headline).X;
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - headlineWidth));
            Widgets.Text(result.Headline, Theme.Text);

            if (result.Evaluation is { } evaluation)
            {
                var odds = evaluation.Optimal;
                var column = width / 3;
                var start = ImGui.GetCursorPosX();
                Odds("1+ line", odds.AtLeastOne, 1, start);
                ImGui.SameLine(start + column);
                Odds("2+ lines", odds.AtLeastTwo, 1, start + column);
                ImGui.SameLine(start + column * 2);
                Odds("3 lines", odds.Three, 2, start + column * 2);
            }
            else
            {
                var lines = LineTable.CountLines(board.StickerMask);
                Widgets.Text(lines == 1 ? "Journal complete: 1 line" : $"Journal complete: {lines} lines", Theme.TextMuted);
            }
        }

        if (ui.Button("overlayOpen", FontAwesomeIcon.BookOpen, "Open in DutyHuddle", primary: true, width: width))
            openJournalPage();
    }

    /// <summary>One odds column: the label above, the percentage below.</summary>
    private static void Odds(string label, double value, int decimals, float x)
    {
        ImGui.BeginGroup();
        Widgets.Text(label, Theme.TextFaint);
        ImGui.SetCursorPosX(x);
        Widgets.Text(StickerAdvisor.Percent(value, decimals), Theme.Gold);
        ImGui.EndGroup();
    }
}
