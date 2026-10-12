using Dalamud.Configuration;
using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Solver;

namespace DutyHuddle.Configuration;

public enum RoomKeyMode
{
    /// <summary>Key derived from the members' content IDs: the relay operator cannot derive it.</summary>
    ContentIds = 0,

    /// <summary>Key derived from Name@World: weaker, anyone who knows the party could derive it.</summary>
    NameAndWorld = 1,
}

public sealed class DurationSampleData
{
    public double AverageMinutes { get; set; }
    public int Count { get; set; }
}

[Serializable]
public sealed class PluginConfig : IPluginConfiguration
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    // Sharing with the party (opt-in). Turning the switch on is the consent: the text next to it says what is shared.
    public bool ShareWithParty { get; set; }

    /// <summary>Server override for advanced users; empty means the address built into the plugin.</summary>
    public string RelayUrl { get; set; } = string.Empty;

    public RoomKeyMode KeyMode { get; set; } = RoomKeyMode.ContentIds;
    public bool HideReceivedChatCodes { get; set; } = true;

    // Version 1 kept two separate flags; read only to migrate.
    public bool SyncEnabled { get; set; }
    public bool SyncConsentGiven { get; set; }

    // Main window.
    public int SelectedPage { get; set; }

    // Daily Roulette tab.
    public bool OnlyQueueableRoulettes { get; set; }
    public bool HideCompletedByEveryone { get; set; }
    public double UnknownWeight { get; set; } = 0.5;
    public bool IncludePvP { get; set; } = true;
    public Dictionary<uint, double> RoulettePriority { get; set; } = [];
    public Dictionary<uint, int> RouletteMinutesOverride { get; set; } = [];
    public Dictionary<uint, DurationSampleData> MeasuredDurations { get; set; } = [];

    // Wondrous Tails tab.
    public bool GroupCategories { get; set; } = true;
    public bool HideUniqueDuties { get; set; }
    public bool NotifyCommonDuty { get; set; } = true;

    // Sticker Advisor tab.
    public AdvisorGoal AdvisorGoal { get; set; } = AdvisorGoal.AtLeastOneLine;

    /// <summary>Objectives the player will not do, per journal ("Name@World|expiry").</summary>
    public Dictionary<string, List<int>> SkippedObjectives { get; set; } = [];

    /// <summary>
    /// Roulettes left out of recommendations unless the player raises their priority:
    /// Guildhests (row 4) are rarely worth anyone's time.
    /// </summary>
    public static readonly IReadOnlyDictionary<uint, double> DefaultPriorities = new Dictionary<uint, double> { [4] = 0 };

    public double GetPriority(uint rouletteId)
        => RoulettePriority.TryGetValue(rouletteId, out var value) ? value : DefaultPriorities.GetValueOrDefault(rouletteId, 1.0);

    public RecommendationSettings ToRecommendationSettings(IEnumerable<uint> catalogRouletteIds) => new()
    {
        UnknownWeight = UnknownWeight,
        IncludePvP = IncludePvP,
        Priority = catalogRouletteIds.ToDictionary(id => id, GetPriority),
    };

    public DurationSample? GetMeasured(uint dutyId)
        => MeasuredDurations.TryGetValue(dutyId, out var data) ? new DurationSample(data.AverageMinutes, data.Count) : null;

    public void AddMeasured(uint dutyId, double minutes)
    {
        var updated = (GetMeasured(dutyId) ?? new DurationSample(0, 0)).Add(minutes);
        MeasuredDurations[dutyId] = new DurationSampleData { AverageMinutes = updated.AverageMinutes, Count = updated.Count };
    }

    /// <summary>Brings an older configuration up to date. Returns true when something changed.</summary>
    public bool Migrate()
    {
        if (Version >= CurrentVersion)
            return false;

        if (Version < 2)
            ShareWithParty = SyncEnabled && SyncConsentGiven;

        Version = CurrentVersion;
        return true;
    }

    /// <summary>Drops per-journal entries whose journal has expired.</summary>
    public void PruneSkipped(DateTime nowUtc)
    {
        foreach (var key in SkippedObjectives.Keys.ToList())
        {
            var separator = key.LastIndexOf('|');
            if (separator < 0 || !long.TryParse(key[(separator + 1)..], out var expiry)
                || DateTimeOffset.FromUnixTimeSeconds(expiry).UtcDateTime < nowUtc)
                SkippedObjectives.Remove(key);
        }
    }
}
