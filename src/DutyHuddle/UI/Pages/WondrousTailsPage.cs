using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Solver;
using DutyHuddle.Services;

namespace DutyHuddle.UI.Pages;

/// <summary>Wondrous Tails in one place: what the party should run (left) and your own journal with the advisor (right).</summary>
public sealed class WondrousTailsPage
{
    private static readonly string[] GoalLabels = ["1 line", "2 lines", "3 lines", "Best reward"];

    private readonly Widgets ui;
    private readonly PluginConfig config;
    private readonly Action saveConfig;
    private readonly DutyCatalog catalog;
    private readonly IReadOnlyDictionary<byte, string> expansions;
    private readonly IDutyFinder dutyFinder;
    private readonly SolverCache solvers;
    private readonly HashSet<string> expandedJournals = new(StringComparer.Ordinal);
    private string search = string.Empty;
    private int expansionFilter = -1;
    private int partySection;
    private WtRankingRow? menuRow;
    private MemberId? viewedMember;

    public WondrousTailsPage(
        Widgets ui,
        PluginConfig config,
        Action saveConfig,
        DutyCatalog catalog,
        IReadOnlyDictionary<byte, string> expansions,
        IDutyFinder dutyFinder,
        SolverCache solvers)
    {
        this.ui = ui;
        this.config = config;
        this.saveConfig = saveConfig;
        this.catalog = catalog;
        this.expansions = expansions;
        this.dutyFinder = dutyFinder;
        this.solvers = solvers;
    }

    public void Draw(PartyView view)
    {
        var avail = ImGui.GetContentRegionAvail();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rightWidth = Math.Clamp(avail.X * 0.42f, Theme.S(340), Theme.S(470));

        if (Widgets.BeginChildCard("##wtParty", new Vector2(avail.X - rightWidth - spacing, 0)))
            DrawParty(view);
        Widgets.EndChildCard();

        ImGui.SameLine();
        if (Widgets.BeginChildCard("##wtMine", Vector2.Zero, Theme.Alpha(Theme.Gold, 0.35f)))
            DrawMyJournal(view);
        Widgets.EndChildCard();
    }

    // ------------------------------------------------------------------ party column

    private void DrawParty(PartyView view)
    {
        var shared = view.Journals.Count(j => j.Board is not null);
        var labels = new[] { "Duties your party needs", $"Party journals ({shared}/{view.Journals.Count})" };
        partySection = Widgets.Segmented("##partySection", labels, partySection, ImGui.GetContentRegionAvail().X);
        ImGui.Spacing();

        if (partySection == 1)
        {
            DrawJournals(view);
            return;
        }

        Widgets.Text("One run of a duty near the top fills the most journals.", Theme.TextMuted);
        ImGui.Spacing();

        ImGui.SetNextItemWidth(Theme.S(190));
        ImGui.InputTextWithHint("##wtSearch", "Search duties", ref search, 64);
        ImGui.SameLine(0, Theme.S(8));
        ImGui.SetNextItemWidth(Theme.S(160));
        var preview = expansionFilter < 0 ? "All expansions" : expansions.GetValueOrDefault((byte)expansionFilter, "?");
        if (ImGui.BeginCombo("##wtExpansion", preview))
        {
            if (ImGui.Selectable("All expansions", expansionFilter < 0))
                expansionFilter = -1;
            foreach (var (id, name) in expansions.OrderBy(kv => kv.Key))
                if (ImGui.Selectable(name, expansionFilter == id))
                    expansionFilter = id;
            ImGui.EndCombo();
        }

        if (Widgets.Chip("##group", "Group similar duties", config.GroupCategories))
        {
            config.GroupCategories = !config.GroupCategories;
            saveConfig();
        }

        ImGui.SameLine(0, Theme.S(6));
        if (Widgets.Chip("##hideUnique", "Only shared duties", config.HideUniqueDuties))
        {
            config.HideUniqueDuties = !config.HideUniqueDuties;
            saveConfig();
        }

        ImGui.Spacing();
        DrawRanking(view);
    }

    private void DrawRanking(PartyView view)
    {
        if (view.Journals.All(j => j.Board is null))
        {
            Widgets.Text("No journal yet. Pick one up from Khloe in Idyllshire.", Theme.TextMuted);
            return;
        }

        var rows = view.Ranking
            .Where(r => !config.HideUniqueDuties || r.Group != CoverageGroup.Unique)
            .Where(r => expansionFilter < 0 || r.Expansions.Contains((byte)expansionFilter))
            .Where(r => search.Length == 0 || Matches(r, search))
            .ToList();
        if (rows.Count == 0)
        {
            Widgets.Text("Nothing matches the filters.", Theme.TextMuted);
            return;
        }

        var draw = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();
        var rowHeight = lineHeight * 2 + Theme.S(16);
        var icon = Theme.S(32);
        var dot = Theme.S(15);
        var dotsWidth = dot * view.Journals.Count + Theme.S(2) * (view.Journals.Count - 1);

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var everyone = row.Group == CoverageGroup.All && row.KnownMembers >= 2;
            var (min, max, hovered, clicked) = Widgets.Row($"##duty{i}", rowHeight, everyone);
            var centerY = (min.Y + max.Y) / 2;

            // Rank badge.
            var badgeCenter = new Vector2(min.X + Theme.S(18), centerY);
            var badgeColor = everyone ? Theme.Gold : row.Group == CoverageGroup.Shared ? Theme.Blue : Theme.Grey;
            draw.AddCircleFilled(badgeCenter, Theme.S(11), Theme.U32(Theme.Alpha(badgeColor, 0.2f)));
            var rank = (i + 1).ToString();
            var rankSize = ImGui.CalcTextSize(rank);
            draw.AddText(badgeCenter - rankSize / 2, Theme.U32(badgeColor), rank);

            ui.DrawGameIcon(draw, new Vector2(min.X + Theme.S(38), centerY - icon / 2), icon, row.IconId);

            var textX = min.X + Theme.S(48) + icon;
            draw.AddText(new Vector2(textX, centerY - lineHeight - Theme.S(1)), Theme.U32(everyone ? Theme.Gold : Theme.Text), row.Label);
            var subtitle = row.IsCategory
                ? $"Any of {row.DutyIds.Count} duties · ~{row.EstimatedMinutes} min"
                : $"Lv. {catalog.GetDuty(row.DutyIds[0])?.Level} · ~{row.EstimatedMinutes} min";
            draw.AddText(new Vector2(textX, centerY + Theme.S(1)), Theme.U32(Theme.TextFaint), subtitle);

            // Who needs it, then the count.
            var countText = $"{row.MemberCount}/{row.KnownMembers}";
            var countWidth = ImGui.CalcTextSize("8/8").X;
            var right = max.X - Theme.S(10);
            draw.AddText(new Vector2(right - countWidth, centerY - lineHeight / 2), Theme.U32(Theme.TextMuted), countText);
            var dotsX = right - countWidth - Theme.S(12) - dotsWidth;
            for (var m = 0; m < view.Journals.Count; m++)
            {
                var journal = view.Journals[m];
                var center = new Vector2(dotsX + m * (dot + Theme.S(2)) + dot / 2, centerY);
                Widgets.DrawCoverageDot(draw, center, dot, journal.Board is null ? null : row.Members.Contains(journal.Member));
            }

            if (hovered)
                Widgets.TooltipNow(RowTooltip(row) + "\n\nClick for options.");
            if (clicked)
            {
                menuRow = row;
                ImGui.OpenPopup("##dutyMenu");
            }
        }

        if (ImGui.BeginPopup("##dutyMenu"))
        {
            DrawDutyMenu();
            ImGui.EndPopup();
        }
    }

    private void DrawDutyMenu()
    {
        if (menuRow is null)
            return;

        ui.Title(menuRow.Label, Theme.Gold);
        Widgets.Text("Needed by " + string.Join(", ", menuRow.Members.Select(m => m.Name)), Theme.TextMuted);
        ImGui.Spacing();
        foreach (var dutyId in menuRow.DutyIds.Take(14))
        {
            if (catalog.GetDuty(dutyId) is not { } duty)
                continue;
            if (ImGui.Selectable($"Open {duty.Name} (Lv. {duty.Level})##open{dutyId}"))
                dutyFinder.OpenDuty(dutyId);
        }

        if (menuRow.DutyIds.Count > 14)
            Widgets.Text($"...and {menuRow.DutyIds.Count - 14} more", Theme.TextFaint);
    }

    private void DrawJournals(PartyView view)
    {
        Widgets.Text("Click a member to see which objectives they still need, or the grid to see their journal.", Theme.TextMuted);
        ImGui.Spacing();
        var draw = ImGui.GetWindowDrawList();
        var lineHeight = ImGui.GetTextLineHeight();
        var avatar = Theme.S(28);
        var rowHeight = avatar + Theme.S(12);
        foreach (var journal in view.Journals)
        {
            var member = view.Members.FirstOrDefault(m => m.Member == journal.Member) ?? new PartyMemberState(journal.Member, journal.IsLocal, null);
            var key = journal.Member.Key;
            var expanded = expandedJournals.Contains(key);

            // Right side, from the edge inwards: points, seals, bar, then the journal button for other members.
            var rowMin = ImGui.GetCursorScreenPos();
            var centerY = rowMin.Y + rowHeight / 2;
            var right = rowMin.X + ImGui.GetContentRegionAvail().X - Theme.S(10);
            var board = journal.Board;
            var pointsText = board is null ? string.Empty : $"{board.SecondChancePoints} pts";
            var pointsWidth = ImGui.CalcTextSize(pointsText).X;
            var sealsX = right - pointsWidth - Theme.S(16) - ImGui.CalcTextSize("9/9").X;
            var barWidth = Theme.S(90);
            var barX = sealsX - Theme.S(8) - barWidth;

            // Submitted before the row so it takes the hover and the click instead of the row.
            var buttonSize = Theme.S(26);
            var buttonMin = new Vector2(barX - Theme.S(12) - buttonSize, centerY - buttonSize / 2);
            var showButton = board is not null && !journal.IsLocal;
            var buttonHovered = false;
            if (showButton)
            {
                ImGui.SetCursorScreenPos(buttonMin);
                if (ImGui.InvisibleButton($"##viewJournal{key}", new Vector2(buttonSize)))
                {
                    viewedMember = journal.Member;
                    ImGui.OpenPopup("##journalView");
                }

                buttonHovered = ImGui.IsItemHovered();
                ImGui.SetCursorScreenPos(rowMin);
            }

            var (min, _, hovered, clicked) = Widgets.Row($"##journal{key}", rowHeight);
            ui.DrawAvatar(draw, new Vector2(min.X + Theme.S(6), centerY - avatar / 2), avatar, member);
            var name = journal.IsLocal ? journal.Member.Name + " (you)" : journal.Member.Name;
            draw.AddText(new Vector2(min.X + avatar + Theme.S(16), centerY - lineHeight / 2), Theme.U32(journal.IsLocal ? Theme.Gold : Theme.Text), name);

            if (board is not null)
            {
                draw.AddText(new Vector2(right - pointsWidth, centerY - lineHeight / 2), Theme.U32(Theme.TextMuted), pointsText);
                draw.AddText(new Vector2(sealsX, centerY - lineHeight / 2), Theme.U32(Theme.Text), $"{board.PlacedSeals}/9");
                Widgets.DrawMiniBar(draw, new Vector2(barX, centerY - Theme.S(3)), new Vector2(barWidth, Theme.S(6)), board.PlacedSeals / 9f, Theme.Blue);
            }
            else
            {
                var text = "not sharing";
                draw.AddText(new Vector2(right - ImGui.CalcTextSize(text).X, centerY - lineHeight / 2), Theme.U32(Theme.TextFaint), text);
            }

            if (showButton)
            {
                DrawJournalButton(draw, buttonMin, buttonSize, buttonHovered);
                if (buttonHovered)
                    Widgets.TooltipNow($"See {journal.Member.Name}'s journal");
            }

            if (hovered && board is not null)
                Widgets.TooltipNow(expanded ? "Click to hide objectives" : "Click to show objectives");
            if (clicked && board is not null && !expandedJournals.Remove(key))
                expandedJournals.Add(key);

            if (expanded && board is not null)
                DrawObjectives(journal);
        }

        if (ImGui.BeginPopup("##journalView"))
        {
            DrawJournalView(view);
            ImGui.EndPopup();
        }
    }

    /// <summary>A small grid glyph: the journal button in a member row.</summary>
    private static void DrawJournalButton(ImDrawListPtr draw, Vector2 min, float size, bool hovered)
    {
        draw.AddRectFilled(min, min + new Vector2(size), Theme.U32(hovered ? Theme.Alpha(Theme.Gold, 0.22f) : Theme.CardRaised), Theme.S(6));
        var color = Theme.U32(hovered ? Theme.Gold : Theme.TextMuted);
        var pad = size * 0.24f;
        var gap = size * 0.06f;
        var cell = (size - pad * 2 - gap * 3) / 4;
        for (var i = 0; i < WtBoard.CellCount; i++)
        {
            var cellMin = min + new Vector2(pad + i % 4 * (cell + gap), pad + i / 4 * (cell + gap));
            draw.AddRectFilled(cellMin, cellMin + new Vector2(cell), color, cell * 0.25f);
        }
    }

    /// <summary>Another member's journal: where their seals are, nothing more.</summary>
    private void DrawJournalView(PartyView view)
    {
        // Looked up every frame so the grid follows new data while it is open.
        var journal = view.Journals.FirstOrDefault(j => j.Member == viewedMember);
        if (journal?.Board is not { } board)
        {
            ImGui.CloseCurrentPopup();
            return;
        }

        ui.Title(journal.Member.Name + "'s journal");
        ImGui.Spacing();
        DrawBoard(board, Theme.S(40));
        ImGui.Spacing();
        var lines = LineTable.CountLines(board.StickerMask);
        var linesText = lines == 1 ? "1 line" : $"{lines} lines";
        Widgets.Text($"{board.PlacedSeals}/9 seals · {linesText} · {board.SecondChancePoints} pts", Theme.TextMuted);
    }

    private void DrawObjectives(WtMemberView journal)
    {
        var size = ImGui.GetTextLineHeight() + Theme.S(2);
        ImGui.Indent(Theme.S(44));
        foreach (var objective in journal.Missing)
        {
            ui.GameIcon(objective.Objective.IconId, size);
            ImGui.SameLine(0, Theme.S(8));
            Widgets.Text(objective.Objective.Text, objective.Objective.IsRecognized ? Theme.Text : Theme.Amber);
            if (!objective.Objective.IsRecognized)
                Widgets.Tooltip("DutyHuddle could not map this objective to duties, so it is left out of the ranking.");
        }

        var done = journal.Done.Select(o => o.Objective.Text).ToList();
        if (done.Count > 0)
            Widgets.Wrapped($"Done ({done.Count}): " + string.Join(", ", done), Theme.TextFaint);
        ImGui.Unindent(Theme.S(44));
        ImGui.Spacing();
    }

    // ------------------------------------------------------------------ my journal column

    private void DrawMyJournal(PartyView view)
    {
        var journal = view.Journals.FirstOrDefault(j => j.IsLocal);
        var board = journal?.Board;
        var width = ImGui.GetContentRegionAvail().X;

        ui.Title("Your journal");
        if (board is not null)
        {
            var left = board.ExpiresUtc - view.ComputedUtc;
            var label = left.TotalDays >= 1 ? $"{(int)left.TotalDays} d {left.Hours} h left" : $"{Math.Max(0, left.Hours)} h left";
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(label).X - Theme.S(16));
            Widgets.Pill(label, left.TotalDays < 2 ? Theme.Amber : Theme.TextMuted);
        }

        if (board is null || journal is null)
        {
            Widgets.Wrapped("No journal. Pick one up from Khloe Aliapoh in Idyllshire, then come back for advice.", Theme.TextMuted);
            return;
        }

        ImGui.Spacing();
        var boardWidth = DrawBoard(board, Math.Min(Theme.S(42), (width * 0.5f - Theme.S(18)) / 4));
        ImGui.SameLine(0, Theme.S(20));
        DrawStats(board, width - boardWidth - Theme.S(20));
        ImGui.Spacing();

        Widgets.Eyebrow("Aim for");
        var goal = Widgets.Segmented("##goal", GoalLabels, (int)config.AdvisorGoal, width);
        if (goal != (int)config.AdvisorGoal)
        {
            config.AdvisorGoal = (AdvisorGoal)goal;
            saveConfig();
        }

        ImGui.Spacing();
        var solver = solvers.TryGet(config.AdvisorGoal);
        if (solver is null)
        {
            using (Widgets.Banner(Theme.Grey))
                Widgets.Text("Calculating exact odds...", Theme.TextMuted);
            return;
        }

        var result = StickerAdvisor.Advise(board, solver, config.SkippedFor(journal.Member, board), view.ComputedUtc);
        DrawAdvice(result);
        ImGui.Spacing();
        DrawOdds(result, width);
        ImGui.Spacing();
        DrawSkipList(journal, board);
    }

    /// <summary>Draws the 4x4 journal at the cursor and returns its width.</summary>
    private static float DrawBoard(WtBoard board, float cell)
    {
        var gap = Theme.S(6);
        var boardWidth = cell * 4 + gap * 3;
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();

        var completedLines = LineTable.Lines.Where(l => (board.StickerMask & l) == l).ToList();
        for (var i = 0; i < WtBoard.CellCount; i++)
        {
            var min = origin + new Vector2(i % 4 * (cell + gap), i / 4 * (cell + gap));
            var max = min + new Vector2(cell);
            var inLine = completedLines.Any(l => (l & (1 << i)) != 0);
            var hasSeal = (board.StickerMask & (1 << i)) != 0;
            draw.AddRectFilled(min, max, Theme.U32(inLine ? Theme.Alpha(Theme.Gold, 0.12f) : Theme.Well), Theme.S(8));
            draw.AddRect(min, max, Theme.U32(inLine ? Theme.Gold : Theme.CardBorder), Theme.S(8), ImDrawFlags.None, inLine ? 2f : 1f);
            if (!hasSeal)
                continue;

            var center = (min + max) / 2;
            var color = inLine ? Theme.Gold : Theme.Blue;
            draw.AddCircleFilled(center, cell * 0.30f, Theme.U32(color));
            draw.AddCircleFilled(center - new Vector2(cell * 0.08f), cell * 0.10f, Theme.U32(Theme.Alpha(Theme.Text, 0.35f)));
        }

        ImGui.Dummy(new Vector2(boardWidth, boardWidth));
        return boardWidth;
    }

    private static void DrawStats(WtBoard board, float width)
    {
        ImGui.BeginGroup();
        Widgets.Eyebrow("Seals");
        Widgets.Text($"{board.PlacedSeals} of 9 placed", Theme.Text);
        Widgets.ProgressBar(board.PlacedSeals / 9f, new Vector2(width, Theme.S(6)), Theme.Blue);
        if (board.ClaimableCount > 0)
            Widgets.Text($"{board.ClaimableCount} seal(s) ready to place", Theme.Green);

        ImGui.Spacing();
        Widgets.Eyebrow("Second Chance");
        Widgets.Text($"{board.SecondChancePoints} of 9 points", Theme.Text);
        Widgets.Pips(board.SecondChancePoints, 9, Theme.Gold, Theme.S(5));

        ImGui.Spacing();
        Widgets.Eyebrow("Lines now");
        var lines = LineTable.CountLines(board.StickerMask);
        Widgets.Text(lines == 0 ? "None yet" : $"{lines} complete", lines > 0 ? Theme.Gold : Theme.Text);
        ImGui.EndGroup();
    }

    private void DrawAdvice(AdvisorResult result)
    {
        var (color, icon) = result.Action switch
        {
            AdvisorAction.UseShuffle => (Theme.Blue, FontAwesomeIcon.Random),
            AdvisorAction.UseRetry => (Theme.Amber, FontAwesomeIcon.Redo),
            AdvisorAction.CompleteBook => (Theme.Gold, FontAwesomeIcon.FlagCheckered),
            AdvisorAction.Continue => (Theme.Green, FontAwesomeIcon.ArrowRight),
            AdvisorAction.NotWorthTheRisk => (Theme.TextMuted, FontAwesomeIcon.ShieldAlt),
            _ => (Theme.TextMuted, FontAwesomeIcon.InfoCircle),
        };

        using (Widgets.Banner(color))
        {
            // Icon in a tinted circle, then the headline in the large heading font.
            var badge = Theme.S(40);
            var origin = ImGui.GetCursorScreenPos();
            var draw = ImGui.GetWindowDrawList();
            var center = origin + new Vector2(badge / 2);
            draw.AddCircleFilled(center, badge / 2, Theme.U32(Theme.Alpha(color, 0.2f)));
            using (ui.Assets.IconFont())
            {
                var iconText = icon.ToIconString();
                draw.AddText(center - ImGui.CalcTextSize(iconText) / 2, Theme.U32(color), iconText);
            }

            ImGui.Dummy(new Vector2(badge));
            ImGui.SameLine(0, Theme.S(12));
            ImGui.BeginGroup();
            Widgets.Eyebrow("Advice");
            ui.Hero(result.Headline, color);
            ImGui.EndGroup();

            Widgets.Wrapped(result.Detail, Theme.Text);
            foreach (var warning in result.Warnings)
                Widgets.Wrapped(warning, Theme.Amber);
        }
    }

    private static void DrawOdds(AdvisorResult result, float width)
    {
        if (result.Evaluation is not { } evaluation)
            return;

        Widgets.Eyebrow("Chance to finish with");
        var labelWidth = width * 0.43f;
        var column = (width - labelWidth) / 3;
        var start = ImGui.GetCursorPosX();
        foreach (var (header, index) in new[] { ("1+ line", 0), ("2+ lines", 1), ("3 lines", 2) })
        {
            ImGui.SameLine(start + labelWidth + column * index);
            Widgets.Text(header, Theme.TextFaint);
        }

        Row("Continue", evaluation.PlaceNext, !evaluation.ShuffleIsBest && evaluation.ShuffleNow is not null);
        if (evaluation.ShuffleNow is { } shuffle)
            Row($"Shuffle now (-{StickerSolver.ShuffleCost} pts)", shuffle, evaluation.ShuffleIsBest);
        Row("Best play from here", evaluation.Optimal, false);

        void Row(string label, LineOdds odds, bool best)
        {
            var color = best ? Theme.Gold : Theme.Text;
            ImGui.SetCursorPosX(start);
            Widgets.Text(label, best ? Theme.Gold : Theme.TextMuted);
            var values = new[] { odds.AtLeastOne, odds.AtLeastTwo, odds.Three };
            for (var i = 0; i < 3; i++)
            {
                ImGui.SameLine(start + labelWidth + column * i);
                Widgets.Text(StickerAdvisor.Percent(values[i], i == 2 ? 2 : 1), color);
            }
        }
    }

    private void DrawSkipList(WtMemberView journal, WtBoard board)
    {
        var open = journal.Objectives.Where(o => o.Status == WtTaskStatus.Open).ToList();
        if (open.Count == 0)
            return;

        var key = PluginConfig.SkipKey(journal.Member, board);
        var skipped = config.SkippedObjectives.GetValueOrDefault(key) ?? [];
        if (!ImGui.CollapsingHeader($"Objectives you won't do ({skipped.Count} of {open.Count})"))
            return;

        Widgets.Wrapped("Mark what you will skip this week (for example Savage). The advice then plans Retries for the missing seals.", Theme.TextMuted);
        foreach (var objective in open)
        {
            var isSkipped = skipped.Contains(objective.Index);
            if (!Widgets.ToggleRow($"##skip{objective.Index}", objective.Objective.Text, ref isSkipped))
                continue;

            if (isSkipped)
                skipped.Add(objective.Index);
            else
                skipped.Remove(objective.Index);
            config.SkippedObjectives[key] = skipped;
            config.PruneSkipped(DateTime.UtcNow);
            saveConfig();
        }
    }

    private bool Matches(WtRankingRow row, string text)
        => row.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
           || row.DutyIds.Any(id => catalog.GetDuty(id)?.Name.Contains(text, StringComparison.OrdinalIgnoreCase) == true);

    private string RowTooltip(WtRankingRow row)
    {
        var lines = new List<string> { "Needed by: " + string.Join(", ", row.Members.Select(m => m.Name)) };
        if (row.IsCategory)
        {
            var duties = row.DutyIds.Select(catalog.GetDuty).OfType<DutyInfo>().ToList();
            lines.Add($"Any of {duties.Count} duties, e.g. " + string.Join(", ", duties.Take(4).Select(d => d.Name)));
        }

        lines.Add("Objective: " + string.Join(" / ", row.ObjectiveTexts));
        return string.Join("\n", lines);
    }
}
