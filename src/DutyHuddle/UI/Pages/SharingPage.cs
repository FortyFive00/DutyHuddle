using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Models;
using DutyHuddle.Services;
using DutyHuddle.Sync;

namespace DutyHuddle.UI.Pages;

/// <summary>One switch, who is sharing with you, and the chat fallback. Technical options stay folded away.</summary>
public sealed class SharingPage
{
    private readonly Widgets ui;
    private readonly PluginConfig config;
    private readonly Action saveConfig;
    private readonly ISharing sharing;
    private DateTime copiedAt = DateTime.MinValue;

    public SharingPage(Widgets ui, PluginConfig config, Action saveConfig, ISharing sharing)
    {
        this.ui = ui;
        this.config = config;
        this.saveConfig = saveConfig;
        this.sharing = sharing;
    }

    public void Draw(PartyView view)
    {
        if (Widgets.BeginChildCard("##sharingScroll", Vector2.Zero, Theme.Alpha(Theme.CardBorder, 0f)))
        {
            DrawSwitch();
            ImGui.Spacing();
            DrawParty(view);
            ImGui.Spacing();
            DrawChat();
            ImGui.Spacing();
            DrawAdvanced();
        }

        Widgets.EndChildCard();
    }

    private void DrawSwitch()
    {
        using var card = Widgets.Card(accent: config.ShareWithParty ? Theme.Green : null);
        var share = config.ShareWithParty;
        if (Widgets.Toggle("##share", ref share))
        {
            config.ShareWithParty = share;
            saveConfig();
        }

        ImGui.SameLine(0, Theme.S(12));
        ui.Title("Share with my party");
        Widgets.Wrapped(
            "Party members who also use DutyHuddle see your daily roulettes and your Wondrous Tails journal, and you see theirs. " +
            "Nothing else is shared. The data is encrypted, so not even the sharing server can read it, and it is deleted two hours after your last update.",
            Theme.TextMuted);
        ImGui.Spacing();

        var (color, text) = sharing.State switch
        {
            SharingState.Off => (Theme.Grey, "Off. You still see your own data, and anything shared through chat."),
            SharingState.NoServer => (Theme.Amber, "On, but this version has no sharing server yet. Use chat sharing below for now."),
            SharingState.Solo => (Theme.Blue, "On. Waiting for you to join a party."),
            SharingState.Connecting => (Theme.Blue, "On. Connecting..."),
            SharingState.Connected => (Theme.Green, "On. Your party data is up to date."),
            _ => (Theme.Amber, "On, but the sharing server can't be reached. Retrying automatically."),
        };
        Widgets.Dot(color, Theme.S(4));
        ImGui.SameLine(0, Theme.S(8));
        Widgets.Wrapped(text, color);
    }

    private void DrawParty(PartyView view)
    {
        using var card = Widgets.Card();
        ui.Title("Your party");
        if (view.Members.Count <= 1)
        {
            Widgets.Text("You are not in a party.", Theme.TextMuted);
            return;
        }

        Widgets.Text("Only members who also run DutyHuddle can share. Everyone else shows as not sharing.", Theme.TextMuted);
        ImGui.Spacing();

        var avatar = Theme.S(32);
        var lineHeight = ImGui.GetTextLineHeight();
        foreach (var member in view.Members)
        {
            var rowStart = ImGui.GetCursorScreenPos();
            ui.Avatar(member, avatar, member.IsLocal ? Theme.Gold : member.Snapshot is null ? null : Theme.Green);
            ImGui.SameLine(0, Theme.S(12));
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, rowStart.Y + (avatar - lineHeight) / 2));
            Widgets.Text(member.IsLocal ? member.Member.Name + " (you)" : member.Member.Name, member.IsLocal ? Theme.Gold : Theme.Text);

            var (label, color) = Status(member, view.ComputedUtc);
            var pillWidth = ImGui.CalcTextSize(label).X + Theme.S(16);
            card.SameLineRight(pillWidth);
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, rowStart.Y + (avatar - ImGui.GetFrameHeight()) / 2));
            Widgets.Pill(label, color);
            ImGui.SetCursorScreenPos(new Vector2(rowStart.X, rowStart.Y + avatar + Theme.S(8)));
        }

        ImGui.Dummy(Vector2.Zero);
    }

    private void DrawChat()
    {
        using var card = Widgets.Card();
        ui.Title("Share through chat");
        Widgets.Wrapped(
            "Works without the server. Copy the message and paste it in party chat (/p). Members with DutyHuddle read it " +
            "automatically; everyone else just sees a short summary of your roulettes and journal.",
            Theme.TextMuted);
        ImGui.Spacing();

        var copied = DateTime.UtcNow - copiedAt < TimeSpan.FromSeconds(3);
        if (ui.Button("copyChat", copied ? FontAwesomeIcon.Check : FontAwesomeIcon.Copy, copied ? "Copied, now paste it in /p" : "Copy chat message", primary: true)
            && sharing.BuildShareMessage(DateTime.UtcNow) is { } message)
        {
            ImGui.SetClipboardText(message);
            copiedAt = DateTime.UtcNow;
        }

        ImGui.Spacing();
        var hide = config.HideReceivedChatCodes;
        if (Widgets.ToggleRow("##hideCodes", "Hide these messages when party members send them", ref hide))
        {
            config.HideReceivedChatCodes = hide;
            saveConfig();
        }
    }

    private void DrawAdvanced()
    {
        if (!ImGui.CollapsingHeader("Advanced"))
            return;

        using var card = Widgets.Card();
        Widgets.Eyebrow("Sharing server");
        var url = config.RelayUrl;
        ImGui.SetNextItemWidth(card.InnerWidth);
        var hint = SyncDefaults.RelayUrl.Length > 0 ? $"Built in: {SyncDefaults.RelayUrl}" : "None built in. Paste an https:// address";
        if (ImGui.InputTextWithHint("##relayUrl", hint, ref url, 256))
        {
            config.RelayUrl = url.Trim();
            saveConfig();
        }

        Widgets.Text("Leave empty to use the server built into DutyHuddle.", Theme.TextFaint);
        ImGui.Spacing();

        var strong = config.KeyMode == RoomKeyMode.ContentIds;
        if (Widgets.ToggleRow("##keyMode", "Stronger encryption (recommended)", ref strong))
        {
            config.KeyMode = strong ? RoomKeyMode.ContentIds : RoomKeyMode.NameAndWorld;
            saveConfig();
        }

        Widgets.Tooltip("The key comes from character IDs that only your party's clients can see; they never leave your computer.");
        ImGui.Spacing();
        if (ui.Button("forget", FontAwesomeIcon.Trash, "Forget data received from others"))
            sharing.ForgetReceived();
    }

    private static (string Label, Vector4 Color) Status(PartyMemberState member, DateTime nowUtc)
    {
        if (member.IsLocal)
            return ("You", Theme.Gold);

        return member.Snapshot switch
        {
            { Source: SnapshotSource.Relay } s => ($"Sharing · {RoulettePage.Age(s.CapturedUtc, nowUtc)}", Theme.Green),
            { Source: SnapshotSource.ChatCode } s => ($"Via chat · {RoulettePage.Age(s.CapturedUtc, nowUtc)}", Theme.Blue),
            { } s => ($"Shared · {RoulettePage.Age(s.CapturedUtc, nowUtc)}", Theme.Blue),
            null => ("Not sharing", Theme.Grey),
        };
    }
}
