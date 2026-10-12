using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dalamud.Plugin.Services;
using DutyHuddle.Domain.Models;

namespace DutyHuddle.Services;

/// <summary>
/// Local log of which roulettes each of your characters completed on which day. Stored only on this
/// computer, one file per character, trimmed to the last 60 days.
/// </summary>
public sealed class HistoryService : DutyHuddle.UI.IRouletteHistory
{
    private const int KeepDays = 60;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string directory;
    private readonly IPluginLog log;
    private readonly Lock gate = new();
    private string? loadedFor;
    private SortedDictionary<string, HashSet<uint>> days = [];

    public HistoryService(string configDirectory, IPluginLog log)
    {
        directory = Path.Combine(configDirectory, "history");
        this.log = log;
    }

    public void Record(MemberSnapshot local)
    {
        var completed = local.Roulettes.Where(kv => kv.Value == Availability.Completed).Select(kv => kv.Key).ToList();
        if (completed.Count == 0)
            return;

        lock (gate)
        {
            Load(local.Member);
            var key = local.DailyEpoch.ToString("yyyy-MM-dd");
            if (!days.TryGetValue(key, out var set))
                days[key] = set = [];
            var added = false;
            foreach (var id in completed)
                added |= set.Add(id);
            if (!added)
                return;

            foreach (var old in days.Keys.Take(Math.Max(0, days.Count - KeepDays)).ToList())
                days.Remove(old);
            Save(local.Member);
        }
    }

    /// <summary>Most recent first: day and the roulettes completed that day.</summary>
    public IReadOnlyList<(DateOnly Day, IReadOnlySet<uint> Roulettes)> Recent(MemberId member, int count)
    {
        lock (gate)
        {
            Load(member);
            return days.Reverse()
                .Take(count)
                .Select(kv => (DateOnly.Parse(kv.Key), (IReadOnlySet<uint>)kv.Value.ToHashSet()))
                .ToList();
        }
    }

    private void Load(MemberId member)
    {
        if (loadedFor == member.Key)
            return;

        loadedFor = member.Key;
        days = [];
        try
        {
            var path = PathFor(member);
            if (File.Exists(path))
                days = JsonSerializer.Deserialize<SortedDictionary<string, HashSet<uint>>>(File.ReadAllText(path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            log.Warning(ex, "Could not read the roulette history.");
        }
    }

    private void Save(MemberId member)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(PathFor(member), JsonSerializer.Serialize(days, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning(ex, "Could not write the roulette history.");
        }
    }

    private string PathFor(MemberId member)
    {
        // File name from a hash so character names never appear on disk in clear.
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(member.Key)).AsSpan(0, 8));
        return Path.Combine(directory, $"{hash}.json");
    }
}
