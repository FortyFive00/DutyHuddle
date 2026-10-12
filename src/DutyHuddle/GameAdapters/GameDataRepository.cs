
using DutyHuddle.Domain.Duty;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace DutyHuddle.GameAdapters;

/// <summary>Reads the Lumina sheets once and turns them into the immutable <see cref="DutyCatalog"/>.</summary>
public sealed class GameDataRepository
{
    private const uint FallbackRouletteIcon = 61807;

    // ContentRoulette row -> duty flag on ContentFinderCondition. Row IDs have been stable since 2.x.
    private static readonly Dictionary<uint, RoulettePool> PoolByRoulette = new()
    {
        [1] = RoulettePool.Leveling,
        [2] = RoulettePool.HighLevel,
        [3] = RoulettePool.MainScenario,
        [4] = RoulettePool.Guildhest,
        [5] = RoulettePool.Expert,
        [6] = RoulettePool.Trial,
        [7] = RoulettePool.Frontline,
        [8] = RoulettePool.LevelCap,
        [9] = RoulettePool.Mentor,
        [15] = RoulettePool.Alliance,
        [17] = RoulettePool.NormalRaid,
    };

    private readonly ISheetSource data;

    public GameDataRepository(ISheetSource data)
    {
        this.data = data;
    }

    public DutyCatalog Load()
    {
        var duties = LoadDuties();
        var roulettes = LoadRoulettes(duties);
        var orders = LoadOrders();
        return new DutyCatalog(duties, roulettes, orders);
    }

    /// <summary>Expansion names (ExVersion sheet), localised to the client language.</summary>
    public IReadOnlyDictionary<byte, string> LoadExpansionNames()
        => data.GetSheet<ExVersion>()
            .Where(x => !x.Name.IsEmpty)
            .ToDictionary(x => (byte)x.RowId, x => Text(x.Name));

    private List<DutyInfo> LoadDuties()
    {
        var result = new List<DutyInfo>();
        foreach (var row in data.GetSheet<ContentFinderCondition>())
        {
            if (row.Name.IsEmpty)
                continue;

            var contentType = row.ContentType.RowId;
            var icon = row.Icon != 0 ? row.Icon : row.ContentType.ValueNullable?.Icon ?? 0;
            result.Add(new DutyInfo
            {
                Id = row.RowId,
                Name = Capitalize(Text(row.Name)),
                IconId = icon,
                Level = row.ClassJobLevelRequired,
                LevelSync = row.ClassJobLevelSync,
                ContentTypeId = contentType,
                MemberTypeId = row.ContentMemberType.RowId,
                InstanceContentId = row.ContentLinkType == 1 ? row.Content.RowId : 0,
                ExpansionId = (byte)(row.TerritoryType.ValueNullable?.ExVersion.RowId ?? 0),
                Roulettes = Flags(row),
            });
        }

        return result;
    }

    private List<RouletteInfo> LoadRoulettes(IReadOnlyCollection<DutyInfo> duties)
    {
        var contentTypes = data.GetSheet<ContentType>();
        var result = new List<RouletteInfo>();
        foreach (var row in data.GetSheet<ContentRoulette>())
        {
            if (row.CompletionArrayIndex < 0 || !row.IsInDutyFinder || row.Name.IsEmpty)
                continue;

            var pool = PoolByRoulette.GetValueOrDefault(row.RowId);

            // Icon: the content type most common in the roulette's pool (dungeon, trial, raid...).
            var dominantType = duties.Where(d => pool != RoulettePool.None && (d.Roulettes & pool) != 0)
                .GroupBy(d => d.ContentTypeId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();
            var icon = dominantType != 0 && contentTypes.TryGetRow(dominantType, out var type) && type.Icon != 0
                ? type.Icon
                : FallbackRouletteIcon;

            result.Add(new RouletteInfo
            {
                Id = row.RowId,
                Name = Text(row.Name),
                IconId = icon,
                RequiredLevel = row.RequiredLevel,
                MembersPerParty = row.ContentMemberType.ValueNullable?.MembersPerParty ?? 0,
                IsPvP = row.IsPvP,
                IsMentorOnly = pool == RoulettePool.Mentor,
                TomestoneReward = row.RewardTomeA + row.RewardTomeB + row.RewardTomeC,
                Pool = pool,
                SortKey = row.SortKey,
            });
        }

        return result;
    }

    private List<WtOrderInfo> LoadOrders()
    {
        var multiple = data.GetSheet<WeeklyBingoMultipleOrder>();
        var result = new List<WtOrderInfo>();
        foreach (var row in data.GetSheet<WeeklyBingoOrderData>())
        {
            if (row.RowId > byte.MaxValue)
                continue;

            var text = row.Text.ValueNullable is { } t ? Text(t.Description) : string.Empty;
            IReadOnlyList<uint> multipleDuties = [];
            if (row.Type == WtObjectiveRules.MultipleDuties && multiple.TryGetRow(row.Data.RowId, out var group))
            {
                multipleDuties = group.Content
                    .Where(c => c.RowId != 0 && c.IsValid)
                    .Select(c => c.Value.ContentFinderCondition.RowId)
                    .Where(id => id != 0)
                    .ToList();
            }

            result.Add(new WtOrderInfo
            {
                Id = (byte)row.RowId,
                Type = row.Type,
                Data = row.Data.RowId,
                MinLevel = row.Unknown2,
                IconId = row.Icon,
                Text = string.IsNullOrWhiteSpace(text) ? $"Objective #{row.RowId}" : text,
                MultipleOrderDuties = multipleDuties,
            });
        }

        return result;
    }

    private static RoulettePool Flags(ContentFinderCondition row)
    {
        var flags = RoulettePool.None;
        if (row.LevelingRoulette) flags |= RoulettePool.Leveling;
        if (row.HighLevelRoulette) flags |= RoulettePool.HighLevel;
        if (row.MSQRoulette) flags |= RoulettePool.MainScenario;
        if (row.GuildHestRoulette) flags |= RoulettePool.Guildhest;
        if (row.ExpertRoulette) flags |= RoulettePool.Expert;
        if (row.TrialRoulette) flags |= RoulettePool.Trial;
        if (row.DailyFrontlineChallenge) flags |= RoulettePool.Frontline;
        if (row.LevelCapRoulette) flags |= RoulettePool.LevelCap;
        if (row.MentorRoulette) flags |= RoulettePool.Mentor;
        if (row.AllianceRoulette) flags |= RoulettePool.Alliance;
        if (row.NormalRaidRoulette) flags |= RoulettePool.NormalRaid;
        return flags;
    }

    private static string Text(ReadOnlySeString value) => value.ExtractText();

    private static string Capitalize(string value)
        => value.Length > 0 && char.IsLower(value[0]) ? char.ToUpperInvariant(value[0]) + value[1..] : value;
}
