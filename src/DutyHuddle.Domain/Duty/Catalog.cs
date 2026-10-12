namespace DutyHuddle.Domain.Duty;

/// <summary>Which daily roulettes a duty belongs to (mirrors the flags on ContentFinderCondition).</summary>
[Flags]
public enum RoulettePool
{
    None = 0,
    Leveling = 1 << 0,
    HighLevel = 1 << 1,
    MainScenario = 1 << 2,
    Guildhest = 1 << 3,
    Expert = 1 << 4,
    Trial = 1 << 5,
    Frontline = 1 << 6,
    LevelCap = 1 << 7,
    Mentor = 1 << 8,
    Alliance = 1 << 9,
    NormalRaid = 1 << 10,
}

/// <summary>Row IDs of the ContentType sheet that the resolver needs.</summary>
public static class ContentTypeIds
{
    public const uint Dungeon = 2;
    public const uint Guildhest = 3;
    public const uint Trial = 4;
    public const uint Raid = 5;
    public const uint PvP = 6;
    public const uint TreasureHunt = 9;
    public const uint DeepDungeon = 21;
}

/// <summary>One entry of the duty finder (a ContentFinderCondition row), reduced to what the analyzers use.</summary>
public sealed record DutyInfo
{
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public uint IconId { get; init; }
    public byte Level { get; init; }
    public byte LevelSync { get; init; }
    public uint ContentTypeId { get; init; }
    public uint MemberTypeId { get; init; }

    /// <summary>InstanceContent row this duty links to, or 0 when it links to something else.</summary>
    public uint InstanceContentId { get; init; }

    public byte ExpansionId { get; init; }
    public RoulettePool Roulettes { get; init; }
}

/// <summary>A daily roulette (a ContentRoulette row with a completion index).</summary>
public sealed record RouletteInfo
{
    public required uint Id { get; init; }
    public required string Name { get; init; }
    public uint IconId { get; init; }
    public byte RequiredLevel { get; init; }

    /// <summary>Largest party that can queue together (ContentMemberType.MembersPerParty).</summary>
    public byte MembersPerParty { get; init; }

    public bool IsPvP { get; init; }
    public bool IsMentorOnly { get; init; }
    public int TomestoneReward { get; init; }
    public RoulettePool Pool { get; init; }
    public byte SortKey { get; init; }

    /// <summary>Name without the "Duty Roulette:" style prefix, for compact columns and chat.</summary>
    public string ShortName => Name.Contains(": ", StringComparison.Ordinal)
        ? Name[(Name.IndexOf(": ", StringComparison.Ordinal) + 2)..]
        : Name;
}

/// <summary>One WeeklyBingoOrderData row.</summary>
public sealed record WtOrderInfo
{
    public required byte Id { get; init; }
    public required uint Type { get; init; }
    public required uint Data { get; init; }

    /// <summary>Lower level bound for range objectives (column Unknown2 in Lumina).</summary>
    public uint MinLevel { get; init; }

    public uint IconId { get; init; }
    public required string Text { get; init; }

    /// <summary>For type 4 objectives: duties listed in WeeklyBingoMultipleOrder.</summary>
    public IReadOnlyList<uint> MultipleOrderDuties { get; init; } = [];
}

/// <summary>Immutable snapshot of the game data the plugin needs, built once from Lumina.</summary>
public sealed class DutyCatalog
{
    private readonly Dictionary<uint, DutyInfo> duties;
    private readonly Dictionary<uint, DutyInfo> byInstance;
    private readonly Dictionary<byte, WtOrderInfo> orders;

    public DutyCatalog(IEnumerable<DutyInfo> duties, IEnumerable<RouletteInfo> roulettes, IEnumerable<WtOrderInfo> orders)
    {
        this.duties = duties.GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        byInstance = this.duties.Values
            .Where(d => d.InstanceContentId != 0)
            .GroupBy(d => d.InstanceContentId)
            .ToDictionary(g => g.Key, g => g.First());
        this.orders = orders.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
        Roulettes = roulettes.OrderBy(r => r.SortKey).ThenBy(r => r.Id).ToList();
    }

    public static DutyCatalog Empty { get; } = new([], [], []);

    public IReadOnlyDictionary<uint, DutyInfo> Duties => duties;

    public IReadOnlyList<RouletteInfo> Roulettes { get; }

    public DutyInfo? GetDuty(uint id) => duties.GetValueOrDefault(id);

    public DutyInfo? GetDutyByInstance(uint instanceContentId) => byInstance.GetValueOrDefault(instanceContentId);

    public WtOrderInfo? GetOrder(byte id) => orders.GetValueOrDefault(id);

    public RouletteInfo? GetRoulette(uint id) => Roulettes.FirstOrDefault(r => r.Id == id);

    public IEnumerable<DutyInfo> GetPool(RouletteInfo roulette)
        => roulette.Pool == RoulettePool.None
            ? []
            : duties.Values.Where(d => (d.Roulettes & roulette.Pool) != 0);
}
