using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;

namespace DutyHuddle.Domain.Analysis;

public enum CoverageGroup
{
    /// <summary>Needed by every member whose journal is known.</summary>
    All,

    /// <summary>Needed by two or more members, but not all.</summary>
    Shared,

    /// <summary>Needed by one member only.</summary>
    Unique,
}

public sealed record WtObjectiveView(int Index, ResolvedObjective Objective, WtTaskStatus Status, bool HasSeal);

public sealed record WtMemberView(MemberId Member, bool IsLocal, WtBoard? Board, IReadOnlyList<WtObjectiveView> Objectives)
{
    public IEnumerable<WtObjectiveView> Missing => Objectives.Where(o => o.Status == WtTaskStatus.Open);

    public IEnumerable<WtObjectiveView> Done => Objectives.Where(o => o.Status != WtTaskStatus.Open);
}

/// <summary>One line of the shared-duty ranking: a concrete duty or a group of interchangeable duties.</summary>
public sealed record WtRankingRow(
    string Label,
    IReadOnlyList<uint> DutyIds,
    IReadOnlyList<MemberId> Members,
    int KnownMembers,
    CoverageGroup Group,
    IReadOnlyList<string> ObjectiveTexts,
    uint IconId,
    int EstimatedMinutes,
    bool IsCategory,
    IReadOnlySet<byte> Expansions)
{
    public int MemberCount => Members.Count;
}

/// <summary>
/// Compares journals by concrete duty, not by objective: "Dungeons (Lv. 90)" for one member and
/// "The Dead Ends" for another are both satisfied by running The Dead Ends.
/// </summary>
public sealed class WtAnalyzer
{
    private readonly DutyCatalog catalog;
    private readonly WtObjectiveResolver resolver;
    private readonly IDurationEstimator durations;

    public WtAnalyzer(DutyCatalog catalog, WtObjectiveResolver resolver, IDurationEstimator durations)
    {
        this.catalog = catalog;
        this.resolver = resolver;
        this.durations = durations;
    }

    public IReadOnlyList<WtMemberView> BuildMemberViews(IReadOnlyList<PartyMemberState> members, DateTime nowUtc)
    {
        var views = new List<WtMemberView>(members.Count);
        foreach (var member in members)
        {
            var board = member.Snapshot?.GetBoard(nowUtc);
            var objectives = board is null
                ? []
                : Enumerable.Range(0, WtBoard.CellCount)
                    .Select(i => new WtObjectiveView(i, resolver.Resolve(board.OrderIds[i]), board.Status[i], (board.StickerMask & (1 << i)) != 0))
                    .ToList();
            views.Add(new WtMemberView(member.Member, member.IsLocal, board, objectives));
        }

        return views;
    }

    public IReadOnlyList<WtRankingRow> Rank(IReadOnlyList<WtMemberView> views, bool groupCategories)
    {
        var known = views.Where(v => v.Board is not null).ToList();
        if (known.Count == 0)
            return [];

        // duty -> (members needing it, objectives it satisfies)
        var cover = new Dictionary<uint, (ulong Mask, SortedSet<byte> Orders)>();
        for (var m = 0; m < known.Count; m++)
        {
            foreach (var objective in known[m].Missing)
            {
                foreach (var dutyId in objective.Objective.DutyIds)
                {
                    if (!cover.TryGetValue(dutyId, out var entry))
                        entry = (0, []);
                    entry.Mask |= 1UL << m;
                    entry.Orders.Add(objective.Objective.OrderId);
                    cover[dutyId] = entry;
                }
            }
        }

        var groups = groupCategories
            ? cover.GroupBy(kv => (kv.Value.Mask, Key: string.Join(',', kv.Value.Orders)))
                .Select(g => (g.Key.Mask, Orders: g.First().Value.Orders, Duties: g.Select(kv => kv.Key).ToList()))
            : cover.Select(kv => (kv.Value.Mask, kv.Value.Orders, Duties: new List<uint> { kv.Key }));

        var rows = new List<WtRankingRow>();
        foreach (var (mask, orders, dutyIds) in groups)
        {
            var duties = dutyIds.Select(catalog.GetDuty).OfType<DutyInfo>().OrderBy(d => d.Level).ThenBy(d => d.Name).ToList();
            if (duties.Count == 0)
                continue;

            var members = Enumerable.Range(0, known.Count).Where(i => (mask & (1UL << i)) != 0).Select(i => known[i].Member).ToList();
            var texts = orders.Select(o => resolver.Resolve(o).Text).Distinct().ToList();
            var isCategory = duties.Count > 1;
            var label = isCategory ? string.Join(" / ", texts) : duties[0].Name;
            var icon = isCategory ? resolver.Resolve(orders.First()).IconId : duties[0].IconId;
            var group = members.Count == known.Count ? CoverageGroup.All
                : members.Count >= 2 ? CoverageGroup.Shared
                : CoverageGroup.Unique;

            rows.Add(new WtRankingRow(
                label,
                duties.Select(d => d.Id).ToList(),
                members,
                known.Count,
                group,
                texts,
                icon,
                duties.Min(durations.EstimateMinutes),
                isCategory,
                duties.Select(d => d.ExpansionId).ToHashSet()));
        }

        return rows
            .OrderByDescending(r => r.MemberCount)
            .ThenBy(r => r.IsCategory)
            .ThenBy(r => r.EstimatedMinutes)
            .ThenBy(r => r.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
