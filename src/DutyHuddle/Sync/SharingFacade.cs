using DutyHuddle.Services;
using DutyHuddle.UI;

namespace DutyHuddle.Sync;

/// <summary>Relay sync and chat codes behind one small interface for the UI.</summary>
public sealed class SharingFacade : ISharing
{
    private readonly SyncService sync;
    private readonly ChatCodeService chatCodes;
    private readonly PartyStateStore store;

    public SharingFacade(SyncService sync, ChatCodeService chatCodes, PartyStateStore store)
    {
        this.sync = sync;
        this.chatCodes = chatCodes;
        this.store = store;
    }

    public SharingState State => sync.State;

    public string Status => sync.Status;

    public bool HasServer => sync.HasServer;

    public string? BuildShareMessage(DateTime nowUtc) => chatCodes.BuildShareMessage(nowUtc);

    public void ForgetReceived() => store.ClearRemote();
}
