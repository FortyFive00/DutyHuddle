namespace DutyHuddle.Sync;

/// <summary>
/// The sharing server built into the plugin (src/DutyHuddle.RelayWorker on Cloudflare Workers).
/// Players never see or type it: they only switch sharing on.
/// </summary>
public static class SyncDefaults
{
    public const string RelayUrl = "https://dutyhuddle-relay.dutyhuddle-relay-worker.workers.dev";

    public static string Effective(string overrideUrl)
        => string.IsNullOrWhiteSpace(overrideUrl) ? RelayUrl : overrideUrl.Trim();
}
