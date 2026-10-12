namespace DutyHuddle.Domain.Duty;

public interface IDurationEstimator
{
    int EstimateMinutes(DutyInfo duty);

    int EstimateMinutes(RouletteInfo roulette);
}

/// <summary>Running average of measured clear times for one duty.</summary>
public sealed record DurationSample(double AverageMinutes, int Count)
{
    public DurationSample Add(double minutes) => new((AverageMinutes * Count + minutes) / (Count + 1), Count + 1);
}

/// <summary>
/// Starts from rough community estimates and switches to the player's own measured clear times
/// once a duty (or a roulette's pool) has enough samples.
/// </summary>
public sealed class DurationEstimator : IDurationEstimator
{
    public const int MinSamples = 3;

    // Rough defaults in minutes. There is no official source; measured times replace them.
    private static readonly Dictionary<RoulettePool, int> RouletteDefaults = new()
    {
        [RoulettePool.Leveling] = 20,
        [RoulettePool.HighLevel] = 22,
        [RoulettePool.MainScenario] = 100,
        [RoulettePool.Guildhest] = 8,
        [RoulettePool.Expert] = 28,
        [RoulettePool.Trial] = 15,
        [RoulettePool.Frontline] = 20,
        [RoulettePool.LevelCap] = 28,
        [RoulettePool.Mentor] = 25,
        [RoulettePool.Alliance] = 40,
        [RoulettePool.NormalRaid] = 18,
    };

    private readonly DutyCatalog catalog;
    private readonly Func<uint, DurationSample?> measured;
    private readonly Func<uint, int?> rouletteOverride;

    public DurationEstimator(DutyCatalog catalog, Func<uint, DurationSample?>? measured = null, Func<uint, int?>? rouletteOverride = null)
    {
        this.catalog = catalog;
        this.measured = measured ?? (_ => null);
        this.rouletteOverride = rouletteOverride ?? (_ => null);
    }

    public int EstimateMinutes(DutyInfo duty)
    {
        if (measured(duty.Id) is { Count: >= MinSamples } sample)
            return Math.Max(1, (int)Math.Round(sample.AverageMinutes));

        if (duty.Roulettes.HasFlag(RoulettePool.Alliance)) return 40;
        if (duty.Roulettes.HasFlag(RoulettePool.NormalRaid)) return 18;

        return duty.ContentTypeId switch
        {
            ContentTypeIds.Dungeon => 25,
            ContentTypeIds.Guildhest => 8,
            ContentTypeIds.Trial => 15,
            ContentTypeIds.Raid => 20,
            ContentTypeIds.PvP => 15,
            ContentTypeIds.TreasureHunt => 15,
            ContentTypeIds.DeepDungeon => 30,
            _ => 20,
        };
    }

    public int EstimateMinutes(RouletteInfo roulette)
    {
        if (rouletteOverride(roulette.Id) is int manual && manual > 0)
            return manual;

        var samples = catalog.GetPool(roulette)
            .Select(d => measured(d.Id))
            .OfType<DurationSample>()
            .Where(s => s.Count > 0)
            .ToList();
        var count = samples.Sum(s => s.Count);
        if (count >= MinSamples)
            return Math.Max(1, (int)Math.Round(samples.Sum(s => s.AverageMinutes * s.Count) / count));

        return RouletteDefaults.GetValueOrDefault(roulette.Pool, 20);
    }
}
