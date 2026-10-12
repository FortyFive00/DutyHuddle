using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;

namespace DutyHuddle.Domain.Analysis;

/// <summary>
/// A party member as the analyzers see it; <see cref="Snapshot"/> is null when nothing is known.
/// <see cref="ClassJobId"/> is only used by the UI (job icon), 0 when unknown.
/// </summary>
public sealed record PartyMemberState(MemberId Member, bool IsLocal, MemberSnapshot? Snapshot, uint ClassJobId = 0);

public sealed record RouletteRow(
    RouletteInfo Roulette,
    IReadOnlyList<Availability> PerMember,
    int Available,
    int Completed,
    int Unknown,
    int Locked)
{
    /// <summary>Members for whom the roulette can be judged (not unknown, not locked).</summary>
    public int Eligible => Available + Completed;
}

public sealed record RecommendationSettings
{
    /// <summary>How much a member with unknown status counts compared to one who certainly has the roulette.</summary>
    public double UnknownWeight { get; init; } = 0.5;

    public bool IncludePvP { get; init; } = true;

    /// <summary>Score multiplier per expected member whose WT objective the roulette's pool would satisfy.</summary>
    public double WtBonusFactor { get; init; } = 0.25;

    /// <summary>Per-roulette priority (default 1). 0 excludes a roulette from recommendations.</summary>
    public IReadOnlyDictionary<uint, double> Priority { get; init; } = new Dictionary<uint, double>();
}

public sealed record RouletteRecommendation(
    RouletteRow Row,
    double Score,
    int Minutes,
    double WtExpectedHits,
    bool PartyCanQueue,
    string Reason);

public static class RouletteAnalyzer
{
    public static IReadOnlyList<RouletteRow> BuildGrid(DutyCatalog catalog, IReadOnlyList<PartyMemberState> members, DateOnly epoch)
    {
        var rows = new List<RouletteRow>(catalog.Roulettes.Count);
        foreach (var roulette in catalog.Roulettes)
        {
            var states = members
                .Select(m => m.Snapshot?.GetRoulette(roulette.Id, epoch) ?? Availability.Unknown)
                .ToList();
            rows.Add(new RouletteRow(
                roulette,
                states,
                states.Count(s => s == Availability.Available),
                states.Count(s => s == Availability.Completed),
                states.Count(s => s == Availability.Unknown),
                states.Count(s => s == Availability.Locked)));
        }

        return rows;
    }
}

/// <summary>Ranks roulettes by party value per minute: who still has it, what it is worth, how long it takes.</summary>
public sealed class RecommendationEngine
{
    private readonly DutyCatalog catalog;
    private readonly WtObjectiveResolver resolver;
    private readonly IDurationEstimator durations;

    public RecommendationEngine(DutyCatalog catalog, WtObjectiveResolver resolver, IDurationEstimator durations)
    {
        this.catalog = catalog;
        this.resolver = resolver;
        this.durations = durations;
    }

    public IReadOnlyList<RouletteRecommendation> Recommend(
        IReadOnlyList<RouletteRow> grid,
        IReadOnlyList<PartyMemberState> members,
        RecommendationSettings settings,
        DateTime nowUtc)
    {
        var partySize = Math.Max(1, members.Count);
        var openDutiesPerMember = members
            .Select(m => m.Snapshot?.GetBoard(nowUtc))
            .OfType<WtBoard>()
            .Select(OpenDuties)
            .ToList();

        var results = new List<RouletteRecommendation>(grid.Count);
        foreach (var row in grid)
        {
            var roulette = row.Roulette;
            var minutes = Math.Max(1, durations.EstimateMinutes(roulette));
            var canQueue = roulette.MembersPerParty == 0 || partySize <= roulette.MembersPerParty;
            var priority = settings.Priority.GetValueOrDefault(roulette.Id, 1.0);
            var wtHits = ExpectedWtHits(roulette, openDutiesPerMember);

            string reason;
            double score;
            if (!canQueue)
            {
                score = 0;
                reason = roulette.MembersPerParty <= 1
                    ? "Solo queue only"
                    : $"Party too large (max {roulette.MembersPerParty})";
            }
            else if (roulette.IsPvP && !settings.IncludePvP)
            {
                score = 0;
                reason = "PvP excluded in settings";
            }
            else if (priority <= 0)
            {
                score = 0;
                reason = "Disabled in settings";
            }
            else if (row.Available == 0)
            {
                score = 0;
                reason = row.Unknown > 0 ? "Nobody is known to have it" : "Nobody has it left";
            }
            else
            {
                var weight = row.Available + settings.UnknownWeight * row.Unknown;
                score = weight * priority / minutes * (1 + settings.WtBonusFactor * wtHits);
                reason = BuildReason(row, members.Count, minutes, wtHits);
            }

            results.Add(new RouletteRecommendation(row, score, minutes, wtHits, canQueue, reason));
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Row.Available)
            .ThenBy(r => r.Minutes)
            .ToList();
    }

    private HashSet<uint> OpenDuties(WtBoard board)
    {
        var set = new HashSet<uint>();
        for (var i = 0; i < WtBoard.CellCount; i++)
            if (board.Status[i] == WtTaskStatus.Open)
                set.UnionWith(resolver.Resolve(board.OrderIds[i]).DutyIds);
        return set;
    }

    /// <summary>Expected number of members whose open WT objective a random duty from the pool would satisfy.</summary>
    private double ExpectedWtHits(RouletteInfo roulette, IReadOnlyList<HashSet<uint>> openDutiesPerMember)
    {
        if (openDutiesPerMember.Count == 0)
            return 0;

        var pool = catalog.GetPool(roulette).Select(d => d.Id).ToList();
        if (pool.Count == 0)
            return 0;

        return openDutiesPerMember.Sum(open => pool.Count(open.Contains) / (double)pool.Count);
    }

    private static string BuildReason(RouletteRow row, int memberCount, int minutes, double wtHits)
    {
        var parts = new List<string> { $"Available for {row.Available} of {memberCount}" };
        if (row.Unknown > 0)
            parts.Add($"{row.Unknown} unknown");
        parts.Add($"about {minutes} min");
        if (wtHits >= 0.05)
            parts.Add($"WT match {wtHits:0.0}");
        return string.Join(" · ", parts);
    }
}
