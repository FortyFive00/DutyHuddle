using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Models;
using DutyHuddle.Services;

namespace DutyHuddle.UI.Pages;

public sealed class RoulettePage
{
    private readonly Widgets ui;
    private readonly PluginConfig config;
    private readonly Action saveConfig;
    private readonly IDutyFinder dutyFinder;
    private readonly IRouletteHistory history;
    private string search = string.Empty;

    public RoulettePage(Widgets ui, PluginConfig config, Action saveConfig, IDutyFinder dutyFinder, IRouletteHistory history)
    {
        this.ui = ui;
        this.config = config;
        this.saveConfig = saveConfig;
        this.dutyFinder = dutyFinder;
        this.history = history;
    }

    public void Draw(PartyView view)
    {
        var ranked = view.Recommendations.Where(r => r.Score > 0).ToList();
        DrawHero(view, ranked);
        ImGui.Spacing();
        DrawToolbar(view);
        DrawGrid(view, ranked.FirstOrDefault());
    }

    private void DrawHero(PartyView view, IReadOnlyList<RouletteRecommendation> ranked)
    {
        using var card = Widgets.Card(accent: Theme.Gold);
        if (ranked.Count == 0)
        {
            Widgets.Eyebrow("Recommended for your party");
            ui.Title("Nothing to recommend right now");
            Widgets.Wrapped(view.Members.Count <= 1
                ? "Join a party and turn on sharing: DutyHuddle will pick the roulette worth the most for everyone."
                : "Everyone has finished the roulettes this party can queue, or nobody's data is known yet.", Theme.TextMuted);
            return;
        }

        var top = ranked[0];
        var rowY = ImGui.GetCursorPosY();
        var iconSize = Theme.S(60);
        ui.GameIcon(top.Row.Roulette.IconId, iconSize);
        ImGui.SameLine(0, Theme.S(16));
        ImGui.BeginGroup();
        Widgets.Eyebrow("Recommended for your party");
        ui.Hero(top.Row.Roulette.ShortName);
        ui.IconPill(FontAwesomeIcon.Clock, $"~{top.Minutes} min", Theme.Blue);
        ImGui.SameLine(0, Theme.S(6));
        ui.IconPill(FontAwesomeIcon.Users, $"{top.Row.Available} of {view.Members.Count} still have it", Theme.Green);
        if (top.Row.Unknown > 0)
        {
            ImGui.SameLine(0, Theme.S(6));
            ui.IconPill(FontAwesomeIcon.Question, $"{top.Row.Unknown} unknown", Theme.Amber);
        }

        if (top.WtExpectedHits >= 0.05)
        {
            ImGui.SameLine(0, Theme.S(6));
            ui.IconPill(FontAwesomeIcon.BookOpen, "Wondrous Tails match", Theme.Gold);
        }

        ImGui.EndGroup();
        var groupHeight = Math.Max(ImGui.GetItemRectSize().Y, iconSize);

        var buttonWidth = ui.ButtonWidth(FontAwesomeIcon.ExternalLinkAlt, "Open in Duty Finder", Theme.S(180));
        card.SameLineRight(buttonWidth);
        ImGui.SetCursorPosY(rowY + (groupHeight - ImGui.GetFrameHeight()) / 2);
        if (ui.Button("openRecommended", FontAwesomeIcon.ExternalLinkAlt, "Open in Duty Finder", primary: true, width: buttonWidth))
            dutyFinder.OpenRoulette(top.Row.Roulette.Id);

        var alternatives = ranked.Skip(1).Take(3).ToList();
        if (alternatives.Count == 0)
            return;

        ImGui.SetCursorPosY(rowY + groupHeight + Theme.S(8));
        Widgets.Separator(card.InnerWidth);
        ImGui.AlignTextToFramePadding();
        Widgets.Text("Also good", Theme.TextMuted);
        foreach (var alternative in alternatives)
        {
            ImGui.SameLine(0, Theme.S(8));
            Widgets.Pill($"{alternative.Row.Roulette.ShortName} · {alternative.Row.Available}/{view.Members.Count} · ~{alternative.Minutes} min", Theme.TextMuted);
        }
    }

    private void DrawToolbar(PartyView view)
    {
        ImGui.SetNextItemWidth(Theme.S(220));
        ImGui.InputTextWithHint("##rouletteSearch", "Search roulettes", ref search, 64);

        ImGui.SameLine(0, Theme.S(10));
        if (Widgets.Chip("##queueable", "Party can queue", config.OnlyQueueableRoulettes))
        {
            config.OnlyQueueableRoulettes = !config.OnlyQueueableRoulettes;
            saveConfig();
        }

        ImGui.SameLine(0, Theme.S(6));
        if (Widgets.Chip("##hideDone", "Hide finished", config.HideCompletedByEveryone))
        {
            config.HideCompletedByEveryone = !config.HideCompletedByEveryone;
            saveConfig();
        }

        var local = view.Members.FirstOrDefault(m => m.IsLocal);
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.GetFrameHeight() - Theme.S(4));
        if (ui.IconButton("history", FontAwesomeIcon.History, "Your roulettes over the last 7 days"))
            ImGui.OpenPopup("##rouletteHistory");

        if (ImGui.BeginPopup("##rouletteHistory"))
        {
            DrawHistory(view, local);
            ImGui.EndPopup();
        }
    }

    private void DrawGrid(PartyView view, RouletteRecommendation? top)
    {
        var members = view.Members;
        var recommendations = view.Recommendations.ToDictionary(r => r.Row.Roulette.Id);
        var rows = view.RouletteGrid
            .Where(r => search.Length == 0 || r.Roulette.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(r => !config.OnlyQueueableRoulettes || recommendations.GetValueOrDefault(r.Roulette.Id)?.PartyCanQueue != false)
            .Where(r => !config.HideCompletedByEveryone || r.Available > 0 || r.Unknown > 0)
            .ToList();

        if (Widgets.BeginChildCard("##rouletteGrid", Vector2.Zero))
        {
            var draw = ImGui.GetWindowDrawList();
            var width = ImGui.GetContentRegionAvail().X;
            var memberColumn = Theme.S(84);
            var totalColumn = Theme.S(104);
            var nameWidth = Math.Max(Theme.S(150), width - memberColumn * members.Count - totalColumn);
            var lineHeight = ImGui.GetTextLineHeight();

            // Header: one avatar per member.
            var avatar = Theme.S(30);
            var headerHeight = avatar + lineHeight + Theme.S(10);
            var origin = ImGui.GetCursorScreenPos();
            draw.AddText(origin + new Vector2(Theme.S(8), headerHeight - lineHeight - Theme.S(4)), Theme.U32(Theme.TextFaint), "ROULETTE");
            for (var i = 0; i < members.Count; i++)
            {
                var member = members[i];
                var centerX = origin.X + nameWidth + memberColumn * i + memberColumn / 2;
                var avatarMin = new Vector2(centerX - avatar / 2, origin.Y);
                ui.DrawAvatar(draw, avatarMin, avatar, member, member.IsLocal ? Theme.Gold : member.Snapshot is null ? null : Theme.Green);
                var name = Truncate(member.Member.Name.Split(' ')[0], memberColumn - Theme.S(8));
                var nameSize = ImGui.CalcTextSize(name);
                draw.AddText(new Vector2(centerX - nameSize.X / 2, origin.Y + avatar + Theme.S(4)), Theme.U32(member.IsLocal ? Theme.Gold : Theme.TextMuted), name);
                HoverTooltip(avatarMin, avatarMin + new Vector2(avatar), MemberTooltip(member, view.ComputedUtc));
            }

            draw.AddText(origin + new Vector2(nameWidth + memberColumn * members.Count + Theme.S(8), headerHeight - lineHeight - Theme.S(4)), Theme.U32(Theme.TextFaint), "PARTY");
            ImGui.Dummy(new Vector2(width, headerHeight));
            Widgets.Separator(width);

            var rowHeight = Theme.S(38);
            var icon = Theme.S(26);
            var glyph = Theme.S(20);
            foreach (var row in rows)
            {
                var recommendation = recommendations.GetValueOrDefault(row.Roulette.Id);
                var canQueue = recommendation?.PartyCanQueue != false;
                var isTop = top is not null && top.Row.Roulette.Id == row.Roulette.Id;
                var (min, max, hovered, _) = Widgets.Row($"##roulette{row.Roulette.Id}", rowHeight, isTop);
                var centerY = (min.Y + max.Y) / 2;

                ui.DrawGameIcon(draw, new Vector2(min.X + Theme.S(10), centerY - icon / 2), icon, row.Roulette.IconId);
                var nameColor = !canQueue ? Theme.TextFaint : isTop ? Theme.Gold : Theme.Text;
                draw.AddText(new Vector2(min.X + Theme.S(20) + icon, centerY - lineHeight / 2), Theme.U32(nameColor), row.Roulette.ShortName);

                for (var i = 0; i < members.Count; i++)
                {
                    var center = new Vector2(min.X + nameWidth + memberColumn * i + memberColumn / 2, centerY);
                    Widgets.DrawStatusGlyph(draw, center, glyph, row.PerMember[i]);
                }

                var known = row.Available + row.Completed;
                var totalX = min.X + nameWidth + memberColumn * members.Count + Theme.S(8);
                Widgets.DrawMiniBar(draw, new Vector2(totalX, centerY - Theme.S(3)), new Vector2(Theme.S(44), Theme.S(6)), known == 0 ? 0 : row.Available / (float)known, Theme.Green);
                draw.AddText(new Vector2(totalX + Theme.S(52), centerY - lineHeight / 2), Theme.U32(Theme.TextMuted), $"{row.Available}/{known}");

                if (hovered)
                    Widgets.TooltipNow(CellTooltip(view, row, recommendation, min.X, nameWidth, memberColumn));
            }

            if (rows.Count == 0)
                Widgets.Text("No roulette matches the filters.", Theme.TextMuted);

            ImGui.Spacing();
            DrawLegend();
        }

        Widgets.EndChildCard();
    }

    private static void DrawLegend()
    {
        var size = ImGui.GetTextLineHeight();
        foreach (var (state, label) in new[]
                 {
                     (Availability.Available, "still available"),
                     (Availability.Completed, "done today"),
                     (Availability.Unknown, "unknown"),
                     (Availability.Locked, "locked"),
                 })
        {
            Widgets.StatusGlyph(state, size);
            ImGui.SameLine(0, Theme.S(4));
            Widgets.Text(label, Theme.TextFaint);
            ImGui.SameLine(0, Theme.S(16));
        }

        ImGui.NewLine();
    }

    private void DrawHistory(PartyView view, PartyMemberState? local)
    {
        ui.Title("Your last 7 days");
        if (local is null)
        {
            Widgets.Text("Log in to see your history.", Theme.TextMuted);
            return;
        }

        var names = view.RouletteGrid.ToDictionary(r => r.Roulette.Id, r => r.Roulette.ShortName);
        var recent = history.Recent(local.Member, 7);
        if (recent.Count == 0)
        {
            Widgets.Text("Nothing recorded yet. Completed roulettes are logged on this computer only.", Theme.TextMuted);
            return;
        }

        foreach (var (day, roulettes) in recent)
        {
            Widgets.Text(day.ToString("ddd dd MMM", System.Globalization.CultureInfo.InvariantCulture), Theme.Gold);
            ImGui.SameLine(Theme.S(110));
            Widgets.Text(string.Join(", ", roulettes.Select(id => names.GetValueOrDefault(id, $"#{id}"))), Theme.Text);
        }
    }

    private static string CellTooltip(PartyView view, RouletteRow row, RouletteRecommendation? recommendation, float rowX, float nameWidth, float memberColumn)
    {
        var mouseX = ImGui.GetMousePos().X - rowX - nameWidth;
        var column = mouseX < 0 ? -1 : (int)(mouseX / memberColumn);
        if (column >= 0 && column < view.Members.Count)
        {
            var member = view.Members[column];
            var text = $"{member.Member.Name}: {Describe(row.PerMember[column])}";
            return member.Snapshot is null ? text + "\nNo data: not sharing with you." : text + "\n" + Source(member.Snapshot, view.ComputedUtc);
        }

        var lines = new List<string> { row.Roulette.Name };
        if (row.Roulette.RequiredLevel > 0)
            lines.Add($"Level {row.Roulette.RequiredLevel}+");
        if (recommendation is not null)
        {
            lines.Add($"About {recommendation.Minutes} min");
            lines.Add(recommendation.PartyCanQueue ? recommendation.Reason : "Can't queue as this party: " + recommendation.Reason);
        }

        return string.Join("\n", lines);
    }

    private static string MemberTooltip(PartyMemberState member, DateTime nowUtc)
        => member.IsLocal ? $"{member.Member.Name} (you)"
            : member.Snapshot is null ? $"{member.Member.Name}\nNot sharing with you"
            : $"{member.Member.Name}\n{Source(member.Snapshot, nowUtc)}";

    private static string Source(MemberSnapshot snapshot, DateTime nowUtc)
    {
        var how = snapshot.Source switch
        {
            SnapshotSource.Local => "Read from your client",
            SnapshotSource.Relay => "Shared automatically",
            SnapshotSource.ChatCode => "Shared through chat",
            _ => "Entered manually",
        };
        return $"{how}, {Age(snapshot.CapturedUtc, nowUtc)}";
    }

    public static string Age(DateTime capturedUtc, DateTime nowUtc)
    {
        var age = nowUtc - capturedUtc;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} h ago";
        return $"{(int)age.TotalDays} d ago";
    }

    private static string Describe(Availability state) => state switch
    {
        Availability.Available => "still available",
        Availability.Completed => "done today",
        Availability.Locked => "not unlocked",
        _ => "unknown",
    };

    private static void HoverTooltip(Vector2 min, Vector2 max, string text)
    {
        if (ImGui.IsMouseHoveringRect(min, max))
            Widgets.TooltipNow(text);
    }

    private static string Truncate(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
            return text;
        while (text.Length > 1 && ImGui.CalcTextSize(text + "...").X > width)
            text = text[..^1];
        return text + "...";
    }
}
