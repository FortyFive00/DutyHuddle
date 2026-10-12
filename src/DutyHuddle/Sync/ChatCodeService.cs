using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Serialization;
using DutyHuddle.Services;

namespace DutyHuddle.Sync;

/// <summary>
/// The no-server fallback. The player copies a share message and pastes it in party chat; clients
/// with the plugin decode messages from party members. Nothing is ever sent automatically.
/// </summary>
public sealed class ChatCodeService : IDisposable
{
    private readonly IChatGui chat;
    private readonly PartyStateStore store;
    private readonly DutyCatalog catalog;
    private readonly PluginConfig config;

    public ChatCodeService(IChatGui chat, PartyStateStore store, DutyCatalog catalog, PluginConfig config)
    {
        this.chat = chat;
        this.store = store;
        this.catalog = catalog;
        this.config = config;
        chat.ChatMessage += OnChatMessage;
    }

    public string? BuildShareMessage(DateTime nowUtc)
        => store.Local is { } local ? ShareMessage.Build(local, catalog, nowUtc) : null;

    public void Dispose() => chat.ChatMessage -= OnChatMessage;

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (message.LogKind is not (XivChatType.Party or XivChatType.CrossParty))
            return;

        if (!ShareMessage.TryExtractCode(message.Message.TextValue, out var payload))
            return;

        var sender = ResolveSender(message);
        if (sender is null || sender.Value == store.Local?.Member)
            return;

        if (SnapshotCodec.TryDecode(payload, sender.Value, SnapshotSource.ChatCode, out var snapshot))
        {
            store.UpdateRemote(snapshot!);
            if (config.HideReceivedChatCodes)
                message.PreventOriginal();
        }
    }

    /// <summary>The sender as a current party member: by player payload first, then by name.</summary>
    private MemberId? ResolveSender(IChatMessage message)
    {
        var members = store.Members;
        var player = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
        if (player is not null)
        {
            var id = new MemberId(player.PlayerName, (ushort)player.World.RowId);
            return members.Any(m => m.Id == id) ? id : null;
        }

        var text = message.Sender.TextValue;
        var matches = members.Where(m => text.Contains(m.Id.Name, StringComparison.Ordinal)).ToList();
        return matches.Count == 1 ? matches[0].Id : null;
    }
}
