using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using DutyHuddle.Domain.Serialization;
using DutyHuddle.Services;

namespace DutyHuddle.Ipc;

/// <summary>
/// Read-only IPC for other plugins running in the same game client. Dalamud IPC never crosses
/// clients: party synchronisation goes through the relay or chat codes instead.
/// </summary>
public sealed class IpcProvider : IDisposable
{
    public const string ApiVersionName = "DutyHuddle.ApiVersion";
    public const string ShareCodeName = "DutyHuddle.GetShareCode";
    public const string CommonDutiesName = "DutyHuddle.GetCommonDuties";
    public const string RecommendedRouletteName = "DutyHuddle.GetRecommendedRoulette";
    public const string StateChangedName = "DutyHuddle.StateChanged";

    private readonly ICallGateProvider<int> apiVersion;
    private readonly ICallGateProvider<string> shareCode;
    private readonly ICallGateProvider<string[]> commonDuties;
    private readonly ICallGateProvider<string> recommendedRoulette;
    private readonly ICallGateProvider<object> stateChanged;

    public IpcProvider(IDalamudPluginInterface pluginInterface, PartyStateStore store, AnalysisService analysis)
    {
        apiVersion = pluginInterface.GetIpcProvider<int>(ApiVersionName);
        apiVersion.RegisterFunc(() => 1);

        shareCode = pluginInterface.GetIpcProvider<string>(ShareCodeName);
        shareCode.RegisterFunc(() => store.Local is { } local
            ? ShareMessage.CodePrefix + SnapshotCodec.ToBase64Url(SnapshotCodec.Encode(local))
            : string.Empty);

        commonDuties = pluginInterface.GetIpcProvider<string[]>(CommonDutiesName);
        commonDuties.RegisterFunc(() => [.. analysis.Current.Ranking.Where(r => r.MemberCount >= 2).Select(r => r.Label)]);

        recommendedRoulette = pluginInterface.GetIpcProvider<string>(RecommendedRouletteName);
        recommendedRoulette.RegisterFunc(() => analysis.Current.Recommendations.FirstOrDefault(r => r.Score > 0)?.Row.Roulette.Name ?? string.Empty);

        stateChanged = pluginInterface.GetIpcProvider<object>(StateChangedName);
    }

    public void NotifyStateChanged() => stateChanged.SendMessage();

    public void Dispose()
    {
        apiVersion.UnregisterFunc();
        shareCode.UnregisterFunc();
        commonDuties.UnregisterFunc();
        recommendedRoulette.UnregisterFunc();
    }
}
