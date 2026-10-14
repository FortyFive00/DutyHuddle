using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using DutyHuddle.Configuration;
using DutyHuddle.Services;
using DutyHuddle.Sync;

namespace DutyHuddle.UI.Pages;

/// <summary>The content of the main window: header, navigation and the selected page.</summary>
public sealed class MainView
{
    public const int RoulettesPage = 0;
    public const int WondrousTailsPage = 1;
    public const int SharingPage = 2;

    private readonly Widgets ui;
    private readonly PluginConfig config;
    private readonly Action saveConfig;
    private readonly ISharing sharing;
    private readonly RoulettePage roulettes;
    private readonly WondrousTailsPage wondrousTails;
    private readonly SharingPage sharingPage;
    private readonly Action openSettings;

    public MainView(
        Widgets ui,
        PluginConfig config,
        Action saveConfig,
        ISharing sharing,
        RoulettePage roulettes,
        WondrousTailsPage wondrousTails,
        SharingPage sharingPage,
        Action openSettings)
    {
        this.ui = ui;
        this.config = config;
        this.saveConfig = saveConfig;
        this.sharing = sharing;
        this.roulettes = roulettes;
        this.wondrousTails = wondrousTails;
        this.sharingPage = sharingPage;
        this.openSettings = openSettings;
    }

    public void Draw(PartyView view)
    {
        DrawHeader(view);
        DrawNavigation();
        ImGui.Spacing();

        if (!config.ShareWithParty && view.Members.Count > 1 && config.SelectedPage != SharingPage)
            DrawSharingHint();

        switch (config.SelectedPage)
        {
            case WondrousTailsPage:
                wondrousTails.Draw(view);
                break;
            case SharingPage:
                sharingPage.Draw(view);
                break;
            default:
                roulettes.Draw(view);
                break;
        }
    }

    private void DrawHeader(PartyView view)
    {
        var startY = ImGui.GetCursorPosY();
        ui.Logo(Theme.S(30));
        ImGui.SameLine(0, Theme.S(10));
        ImGui.BeginGroup();
        ui.Title("DutyHuddle", Theme.Gold);
        var sharingMembers = view.Members.Count(m => !m.IsLocal && m.Snapshot is not null);
        var subtitle = view.Members.Count <= 1
            ? "Solo · showing your own data"
            : $"Party of {view.Members.Count} · {sharingMembers} sharing with you";
        Widgets.Text(subtitle, Theme.TextMuted);
        ImGui.EndGroup();
        var headerHeight = ImGui.GetItemRectSize().Y;

        // Right side: sharing status, chat share, settings.
        var (statusColor, statusIcon) = sharing.State switch
        {
            SharingState.Connected => (Theme.Green, FontAwesomeIcon.Wifi),
            SharingState.Connecting or SharingState.Solo => (Theme.Blue, FontAwesomeIcon.Wifi),
            SharingState.Off => (Theme.Grey, FontAwesomeIcon.EyeSlash),
            _ => (Theme.Amber, FontAwesomeIcon.ExclamationTriangle),
        };

        var buttonWidth = ImGui.GetFrameHeight() + Theme.S(4);
        var pillWidth = Theme.S(210);
        var right = ImGui.GetWindowContentRegionMax().X;
        ImGui.SameLine(right - pillWidth - buttonWidth * 2 - Theme.S(8));
        ImGui.SetCursorPosY(startY + (headerHeight - ImGui.GetFrameHeight()) / 2);
        if (StatusPill(statusIcon, sharing.Status, statusColor, pillWidth))
            Navigate(SharingPage);
        Widgets.Tooltip("Who you are sharing with, and how. Click to open Sharing.");

        ImGui.SameLine();
        ImGui.SetCursorPosY(startY + (headerHeight - ImGui.GetFrameHeight()) / 2);
        if (ui.IconButton("copyShare", FontAwesomeIcon.Comment, "Copy a chat message with your data, to paste in /party.")
            && sharing.BuildShareMessage(DateTime.UtcNow) is { } message)
            ImGui.SetClipboardText(message);

        ImGui.SameLine();
        ImGui.SetCursorPosY(startY + (headerHeight - ImGui.GetFrameHeight()) / 2);
        if (ui.IconButton("settings", FontAwesomeIcon.Cog, "Settings"))
            openSettings();

        ImGui.SetCursorPosY(startY + headerHeight + Theme.S(6));
    }

    private void DrawNavigation()
    {
        var pages = new (FontAwesomeIcon Icon, string Label)[]
        {
            (FontAwesomeIcon.Dice, "Roulettes"),
            (FontAwesomeIcon.BookOpen, "Wondrous Tails"),
            (FontAwesomeIcon.UserFriends, "Sharing"),
        };

        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        for (var i = 0; i < pages.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, Theme.S(18));
            if (ui.NavTab($"##nav{i}", pages[i].Icon, pages[i].Label, config.SelectedPage == i))
                Navigate(i);
        }

        var bottom = ImGui.GetItemRectMax().Y;
        ImGui.GetWindowDrawList().AddLine(new Vector2(origin.X, bottom), new Vector2(origin.X + width, bottom), Theme.U32(Theme.CardBorder), 1f);
    }

    /// <summary>Shown in a party while sharing is off: the one step that makes the party columns fill up.</summary>
    private void DrawSharingHint()
    {
        using var card = Widgets.Banner(Theme.Blue);
        ImGui.AlignTextToFramePadding();
        Widgets.Text("Turn on sharing to see what your party members still have.", Theme.Text);
        var width = ui.ButtonWidth(FontAwesomeIcon.UserFriends, "Open Sharing");
        card.SameLineRight(width);
        if (ui.Button("openSharing", FontAwesomeIcon.UserFriends, "Open Sharing"))
            Navigate(SharingPage);
    }

    private void Navigate(int page)
    {
        if (config.SelectedPage == page)
            return;
        config.SelectedPage = page;
        saveConfig();
    }

    private bool StatusPill(FontAwesomeIcon icon, string label, Vector4 color, float width)
    {
        var height = ImGui.GetFrameHeight();
        var origin = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##sharingPill", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), Theme.U32(Theme.Alpha(color, hovered ? 0.22f : 0.13f)), height / 2);

        string iconText;
        Vector2 iconSize;
        using (ui.Assets.IconFont())
        {
            iconText = icon.ToIconString();
            iconSize = ImGui.CalcTextSize(iconText);
            draw.AddText(origin + new Vector2(Theme.S(12), (height - iconSize.Y) / 2), Theme.U32(color), iconText);
        }

        var textSize = ImGui.CalcTextSize(label);
        draw.AddText(origin + new Vector2(Theme.S(12) + iconSize.X + Theme.S(8), (height - textSize.Y) / 2), Theme.U32(Theme.Text), label);
        return clicked;
    }
}
