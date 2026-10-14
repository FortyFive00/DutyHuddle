using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Time;

namespace DutyHuddle.Domain.Tests;

public class AnalysisTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Epoch = GameClock.DailyEpoch(Now);

    private readonly DutyCatalog catalog = TestCatalog.Build();
    private readonly WtObjectiveResolver resolver;
    private readonly DurationEstimator durations;

    public AnalysisTests()
    {
        resolver = new WtObjectiveResolver(catalog);
        durations = new DurationEstimator(catalog);
    }

    [Fact]
    public void Specific_duty_resolves_through_instance_content()
    {
        var resolved = resolver.Resolve(TestCatalog.OrderDeadEnds);

        Assert.True(resolved.IsRecognized);
        Assert.Equal([TestCatalog.DeadEnds], resolved.DutyIds);
        Assert.Equal("The Dead Ends", resolved.Text);
    }

    [Fact]
    public void Level_range_uses_min_and_max_and_roulette_flags()
    {
        Assert.Equal(
            new HashSet<uint> { TestCatalog.Sastasha, TestCatalog.TamTara },
            resolver.Resolve(TestCatalog.OrderDungeons1To49).DutyIds.ToHashSet());
        Assert.Equal(
            new HashSet<uint> { TestCatalog.DeadEnds, TestCatalog.Aetherfont },
            resolver.Resolve(TestCatalog.OrderDungeons90).DutyIds.ToHashSet());
    }

    [Fact]
    public void Main_scenario_duties_do_not_count_as_level_50_dungeons()
    {
        var resolved = resolver.Resolve(TestCatalog.OrderDungeons50);

        Assert.DoesNotContain(TestCatalog.CastrumMeridianum, resolved.DutyIds);
        Assert.False(resolved.IsRecognized);
    }

    [Fact]
    public void Special_and_multiple_objectives_resolve()
    {
        Assert.Equal([TestCatalog.Frontline], resolver.Resolve(TestCatalog.OrderFrontline).DutyIds);
        Assert.Equal([TestCatalog.PalaceOfTheDead], resolver.Resolve(TestCatalog.OrderDeepDungeons).DutyIds);
        Assert.Equal(
            new HashSet<uint> { TestCatalog.CopiedFactory, TestCatalog.PuppetsBunker },
            resolver.Resolve(TestCatalog.OrderShbAlliance).DutyIds.ToHashSet());
    }

    [Fact]
    public void Unknown_order_is_reported_as_unrecognized()
    {
        var resolved = resolver.Resolve(250);

        Assert.False(resolved.IsRecognized);
        Assert.Empty(resolved.DutyIds);
    }

    [Fact]
    public void Ranking_puts_the_duty_everyone_needs_first()
    {
        var analyzer = new WtAnalyzer(catalog, resolver, durations);
        var members = new[]
        {
            Member("Tu", TestCatalog.OrderDeadEnds, TestCatalog.OrderEndsinger, TestCatalog.OrderP12N),
            Member("Kaia", TestCatalog.OrderDungeons90, TestCatalog.OrderP12N, TestCatalog.OrderDungeons1To49),
            Member("Rhen", TestCatalog.OrderDeadEnds, TestCatalog.OrderEndsinger, TestCatalog.OrderDungeons1To49),
            new PartyMemberState(new MemberId("Mira", 1), false, null),
        };

        var views = analyzer.BuildMemberViews(members, Now);
        var ranking = analyzer.Rank(views, groupCategories: true);

        var first = ranking[0];
        Assert.Equal("The Dead Ends", first.Label);
        Assert.Equal(3, first.MemberCount);
        Assert.Equal(3, first.KnownMembers);
        Assert.Equal(CoverageGroup.All, first.Group);

        var endsinger = ranking.Single(r => r.DutyIds.Contains(TestCatalog.Endsinger));
        Assert.Equal(CoverageGroup.Shared, endsinger.Group);

        var lowLevel = ranking.Single(r => r.DutyIds.Contains(TestCatalog.Sastasha));
        Assert.True(lowLevel.IsCategory);
        Assert.Equal("Dungeons (Lv. 1-49)", lowLevel.Label);
        Assert.Equal(2, lowLevel.DutyIds.Count);

        var aetherfont = ranking.Single(r => r.DutyIds.Contains(TestCatalog.Aetherfont));
        Assert.Equal(CoverageGroup.Unique, aetherfont.Group);
    }

    [Fact]
    public void Ungrouped_ranking_lists_every_duty_separately()
    {
        var analyzer = new WtAnalyzer(catalog, resolver, durations);
        var views = analyzer.BuildMemberViews([Member("Tu", TestCatalog.OrderDungeons1To49)], Now);

        var ranking = analyzer.Rank(views, groupCategories: false);

        Assert.Equal(2, ranking.Count);
        Assert.All(ranking, r => Assert.False(r.IsCategory));
    }

    [Fact]
    public void Member_view_splits_missing_and_done()
    {
        var analyzer = new WtAnalyzer(catalog, resolver, durations);
        var view = analyzer.BuildMemberViews([Member("Tu", TestCatalog.OrderDeadEnds)], Now).Single();

        Assert.Single(view.Missing);
        Assert.Equal(15, view.Done.Count());
    }

    [Fact]
    public void Recommendation_prefers_what_everyone_still_has_and_respects_party_size()
    {
        var engine = new RecommendationEngine(catalog, resolver, durations);
        var members = new[]
        {
            Roulettes("A", (1, Availability.Completed), (2, Availability.Available), (6, Availability.Available), (15, Availability.Available)),
            Roulettes("B", (1, Availability.Available), (2, Availability.Completed), (6, Availability.Available), (15, Availability.Available)),
            Roulettes("C", (1, Availability.Available), (2, Availability.Available), (6, Availability.Available), (15, Availability.Completed)),
            Roulettes("D", (1, Availability.Available), (2, Availability.Available), (6, Availability.Available), (15, Availability.Available)),
            Roulettes("E", (1, Availability.Available), (2, Availability.Available), (6, Availability.Available), (15, Availability.Available)),
        };

        var grid = RouletteAnalyzer.BuildGrid(catalog, members, Epoch);
        var ranked = engine.Recommend(grid, members, new RecommendationSettings(), Now);

        // Five members cannot queue a 4-player roulette; Trials (5/5) beats Alliance Raids (4/5).
        Assert.Equal(6u, ranked[0].Row.Roulette.Id);
        Assert.False(ranked.Single(r => r.Row.Roulette.Id == 1).PartyCanQueue);
        Assert.Equal(0, ranked.Single(r => r.Row.Roulette.Id == 2).Score);
    }

    [Fact]
    public void Unknown_members_count_half_and_pvp_can_be_excluded()
    {
        var engine = new RecommendationEngine(catalog, resolver, durations);
        var members = new[]
        {
            Roulettes("A", (6, Availability.Available), (7, Availability.Available)),
            new PartyMemberState(new MemberId("Ghost", 1), false, null),
        };

        var grid = RouletteAnalyzer.BuildGrid(catalog, members, Epoch);
        var trials = engine.Recommend(grid, members, new RecommendationSettings(), Now).Single(r => r.Row.Roulette.Id == 6);
        var noPvp = engine.Recommend(grid, members, new RecommendationSettings { IncludePvP = false }, Now).Single(r => r.Row.Roulette.Id == 7);

        Assert.Equal(1.5 / 15, trials.Score, 6);
        Assert.Equal(0, noPvp.Score);
    }

    [Fact]
    public void Snapshot_from_before_the_reset_turns_completed_into_available()
    {
        var snapshot = new MemberSnapshot(new MemberId("A", 1), Now.AddDays(-1), Epoch.AddDays(-1),
            new Dictionary<uint, Availability> { [1] = Availability.Completed, [9] = Availability.Locked },
            null, SnapshotSource.Relay);

        Assert.Equal(Availability.Available, snapshot.GetRoulette(1, Epoch));
        Assert.Equal(Availability.Locked, snapshot.GetRoulette(9, Epoch));
        Assert.Equal(Availability.Unknown, snapshot.GetRoulette(2, Epoch));
    }

    private static PartyMemberState Member(string name, params byte[] openOrders)
    {
        var orders = new byte[16];
        var status = new WtTaskStatus[16];
        for (var i = 0; i < 16; i++)
        {
            orders[i] = i < openOrders.Length ? openOrders[i] : (byte)(100 + i);
            status[i] = i < openOrders.Length ? WtTaskStatus.Open : WtTaskStatus.Claimed;
        }

        var board = new WtBoard
        {
            OrderIds = orders,
            Status = status,
            StickerMask = 0,
            SecondChancePoints = 2,
            ExpiresUtc = Now.AddDays(7),
        };
        var snapshot = new MemberSnapshot(new MemberId(name, 1), Now, Epoch, new Dictionary<uint, Availability>(), board, SnapshotSource.Relay);
        return new PartyMemberState(snapshot.Member, name == "Tu", snapshot);
    }

    private static PartyMemberState Roulettes(string name, params (uint Id, Availability State)[] states)
    {
        var snapshot = new MemberSnapshot(new MemberId(name, 1), Now, Epoch,
            states.ToDictionary(s => s.Id, s => s.State), null, SnapshotSource.Relay);
        return new PartyMemberState(snapshot.Member, false, snapshot);
    }
}
