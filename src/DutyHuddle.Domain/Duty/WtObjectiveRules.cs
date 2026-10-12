namespace DutyHuddle.Domain.Duty;

/// <summary>
/// The only place that knows how WeeklyBingoOrderData.Type maps to duties.
/// Verified against the live sheet: for every range type, <c>Unknown2</c> holds the lower level
/// bound and <c>Data</c> the upper one (for example "Dungeons (Lv. 51-59)" has 51 and 59).
/// </summary>
public static class WtObjectiveRules
{
    public const uint SpecificDuty = 0;
    public const uint DungeonExactLevel = 1;
    public const uint DungeonLevelRange = 2;
    public const uint SpecialContent = 3;
    public const uint MultipleDuties = 4;
    public const uint LevelingDungeons = 5;
    public const uint HighLevelDungeons = 6;
    public const uint Trials = 7;
    public const uint AllianceRaids = 8;
    public const uint NormalRaids = 9;

    // Type 3 objectives: Data selects a content family.
    private const uint SpecialCrystallineConflict = 5;
    private const uint SpecialFrontline = 6;
    private const uint SpecialDeepDungeon = 9;
    private const uint SpecialTreasureDungeon = 10;
    private const uint SpecialRivalWings = 12;

    // ContentMemberType rows used by PvP modes.
    private const uint MemberTypeFrontline = 7;
    private const uint MemberTypeRivalWings = 18;
    private const uint MemberTypeCrystallineCasual = 29;
    private const uint MemberTypeCrystallineRanked = 30;

    /// <summary>Predicate for rule-based objectives; null for types 0 and 4 or unknown types.</summary>
    public static Func<DutyInfo, bool>? GetPredicate(WtOrderInfo order)
    {
        var max = order.Data;
        var min = order.MinLevel > 0 ? order.MinLevel : order.Data;

        return order.Type switch
        {
            // Castrum Meridianum and The Praetorium (main scenario roulette) do not count as dungeons.
            DungeonExactLevel => d => IsPlainDungeon(d) && d.Level == order.Data,
            DungeonLevelRange => d => IsPlainDungeon(d) && InRange(d, min, max),
            LevelingDungeons => d => IsPlainDungeon(d) && d.Roulettes.HasFlag(RoulettePool.Leveling) && InRange(d, min, max),
            HighLevelDungeons => d => IsPlainDungeon(d)
                && (d.Roulettes & (RoulettePool.HighLevel | RoulettePool.Expert)) != 0
                && InRange(d, min, max),
            Trials => d => d.Roulettes.HasFlag(RoulettePool.Trial) && InRange(d, min, max),
            AllianceRaids => d => d.Roulettes.HasFlag(RoulettePool.Alliance) && InRange(d, min, max),
            NormalRaids => d => d.Roulettes.HasFlag(RoulettePool.NormalRaid) && InRange(d, min, max),
            SpecialContent => GetSpecialPredicate(order.Data),
            _ => null,
        };
    }

    private static Func<DutyInfo, bool>? GetSpecialPredicate(uint data) => data switch
    {
        SpecialCrystallineConflict => d => d.ContentTypeId == ContentTypeIds.PvP
            && d.MemberTypeId is MemberTypeCrystallineCasual or MemberTypeCrystallineRanked,
        SpecialFrontline => d => d.ContentTypeId == ContentTypeIds.PvP && d.MemberTypeId == MemberTypeFrontline,
        SpecialDeepDungeon => d => d.ContentTypeId == ContentTypeIds.DeepDungeon,
        SpecialTreasureDungeon => d => d.ContentTypeId == ContentTypeIds.TreasureHunt,
        SpecialRivalWings => d => d.ContentTypeId == ContentTypeIds.PvP && d.MemberTypeId == MemberTypeRivalWings,
        _ => null,
    };

    private static bool IsPlainDungeon(DutyInfo d)
        => d.ContentTypeId == ContentTypeIds.Dungeon && !d.Roulettes.HasFlag(RoulettePool.MainScenario);

    private static bool InRange(DutyInfo d, uint min, uint max) => d.Level >= min && d.Level <= max;
}
