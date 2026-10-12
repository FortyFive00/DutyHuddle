using System.Numerics;
using DutyHuddle.Domain.Models;
using DutyHuddle.Domain.Solver;

namespace DutyHuddle.Domain.Tests;

public class SolverTests
{
    // Built once: each solver enumerates ~506k states.
    private static readonly Lazy<StickerSolver> OneLine = new(() => new StickerSolver(AdvisorGoal.AtLeastOneLine));
    private static readonly Lazy<StickerSolver> TwoLines = new(() => new StickerSolver(AdvisorGoal.AtLeastTwoLines));
    private static readonly Lazy<StickerSolver> ThreeLines = new(() => new StickerSolver(AdvisorGoal.ThreeLines));

    /// <summary>Row-major grid: (0,0) (0,2) (1,1) (1,3) (2,0) (2,2) (3,1), the layout used in the design mockup.</summary>
    private const ushort SpreadSeven = (1 << 0) | (1 << 2) | (1 << 5) | (1 << 7) | (1 << 8) | (1 << 10) | (1 << 13);

    [Fact]
    public void Grid_has_ten_lines_and_full_board_completes_all()
    {
        Assert.Equal(10, LineTable.Lines.Length);
        Assert.Equal(10, LineTable.CountLines(0xFFFF));
        Assert.Equal(0, LineTable.CountLines(0));
    }

    [Fact]
    public void Nine_seal_boards_match_known_counts()
    {
        var counts = new int[5];
        for (var mask = 0; mask < 1 << 16; mask++)
            if (BitOperations.PopCount((uint)mask) == 9)
                counts[LineTable.CountLines((ushort)mask)]++;

        // 11,440 boards; exactly 24 give three lines (Console Games Wiki, "3 Lines Reward").
        Assert.Equal([4752, 5480, 1184, 24, 0], counts);
    }

    [Theory]
    [InlineData(0, 0.5846, 0.1056, 0.0021)]
    [InlineData(2, 0.6779, 0.1478, 0.0040)]
    [InlineData(4, 0.7410, 0.1795, 0.0059)]
    [InlineData(6, 0.7905, 0.2052, 0.0075)]
    [InlineData(8, 0.8302, 0.2266, 0.0091)]
    public void Optimal_odds_from_an_empty_journal_match_the_design_table(int points, double one, double two, double three)
    {
        // Reference values come from the independent Python prototype, printed to 4 decimals.
        const double tolerance = 0.00006;
        Assert.Equal(one, OneLine.Value.Evaluate(0, points).OptimalValue, tolerance);
        Assert.Equal(two, TwoLines.Value.Evaluate(0, points).OptimalValue, tolerance);
        Assert.Equal(three, ThreeLines.Value.Evaluate(0, points).OptimalValue, tolerance);
    }

    [Fact]
    public void Ninth_point_is_useless_for_shuffling()
    {
        Assert.Equal(OneLine.Value.Evaluate(0, 8).OptimalValue, OneLine.Value.Evaluate(0, 9).OptimalValue, 9);
    }

    [Fact]
    public void Spread_layout_should_shuffle()
    {
        var evaluation = OneLine.Value.Evaluate(SpreadSeven, 4);

        const double tolerance = 0.0006;
        Assert.True(evaluation.ShuffleIsBest);
        Assert.Equal(0.389, evaluation.PlaceNext.AtLeastOne, tolerance);
        Assert.Equal(0.0, evaluation.PlaceNext.AtLeastTwo, tolerance);
        Assert.NotNull(evaluation.ShuffleNow);
        Assert.Equal(0.678, evaluation.ShuffleNow!.Value.AtLeastOne, tolerance);
        Assert.Equal(0.143, evaluation.ShuffleNow.Value.AtLeastTwo, tolerance);
    }

    [Fact]
    public void Good_layout_should_not_shuffle()
    {
        // Main diagonal complete plus (0,1) (0,2) (3,0): one line already secured.
        const ushort good = (1 << 0) | (1 << 5) | (1 << 10) | (1 << 15) | (1 << 1) | (1 << 2) | (1 << 12);
        var evaluation = OneLine.Value.Evaluate(good, 4);

        Assert.False(evaluation.ShuffleIsBest);
        Assert.Equal(1.0, evaluation.PlaceNext.AtLeastOne, 6);
    }

    [Fact]
    public void Shuffle_is_unavailable_outside_three_to_seven_seals()
    {
        Assert.Null(OneLine.Value.Evaluate(0b11, 9).ShuffleNow);
        Assert.Null(OneLine.Value.Evaluate(0xFF, 9).ShuffleNow);
        Assert.NotNull(OneLine.Value.Evaluate(0b111, 2).ShuffleNow);
        Assert.Null(OneLine.Value.Evaluate(0b111, 1).ShuffleNow);
    }

    [Fact]
    public void Monte_Carlo_simulation_of_the_policy_agrees_with_the_exact_value()
    {
        var solver = OneLine.Value;
        var random = new Random(12345);
        const int runs = 20_000;
        const int startPoints = 4;
        var hits = 0;

        for (var run = 0; run < runs; run++)
        {
            ushort mask = 0;
            var points = startPoints;
            while (BitOperations.PopCount(mask) < 9)
            {
                var seals = BitOperations.PopCount(mask);
                if (StickerSolver.CanShuffle(seals, points) && solver.Evaluate(mask, points).ShuffleIsBest)
                {
                    mask = RandomMask(random, seals);
                    points -= StickerSolver.ShuffleCost;
                    continue;
                }

                mask = PlaceRandom(random, mask);
            }

            if (LineTable.CountLines(mask) >= 1)
                hits++;
        }

        var expected = solver.Evaluate(0, startPoints).OptimalValue;
        var observed = hits / (double)runs;
        var sigma = Math.Sqrt(expected * (1 - expected) / runs);
        Assert.InRange(observed, expected - 4 * sigma, expected + 4 * sigma);
    }

    [Fact]
    public void Advisor_recommends_shuffle_for_a_bad_layout()
    {
        var board = Board(SpreadSeven, points: 4, openDoable: 9);
        var result = StickerAdvisor.Advise(board, OneLine.Value, new HashSet<int>(), DateTime.UtcNow);

        Assert.Equal(AdvisorAction.UseShuffle, result.Action);
    }

    [Fact]
    public void Advisor_says_complete_when_nine_seals_are_placed()
    {
        var board = Board(0x01FF, points: 0, openDoable: 7);
        var result = StickerAdvisor.Advise(board, OneLine.Value, new HashSet<int>(), DateTime.UtcNow);

        Assert.Equal(AdvisorAction.CompleteBook, result.Action);
    }

    [Fact]
    public void Advisor_reports_expired_and_missing_journals()
    {
        var expired = Board(0, points: 0, openDoable: 16) with { ExpiresUtc = DateTime.UtcNow.AddDays(-1) };

        Assert.Equal(AdvisorAction.NoJournal, StickerAdvisor.Advise(null, OneLine.Value, new HashSet<int>(), DateTime.UtcNow).Action);
        Assert.Equal(AdvisorAction.JournalExpired, StickerAdvisor.Advise(expired, OneLine.Value, new HashSet<int>(), DateTime.UtcNow).Action);
    }

    [Fact]
    public void Retry_plan_reserves_points_for_missing_seals()
    {
        // 5 seals placed, 11 open objectives of which 9 are skipped: 2 doable, 4 needed.
        var board = Board(0b11111, points: 5, openDoable: 11);
        var skipped = Enumerable.Range(5, 9).ToHashSet();
        var plan = RetryPlan.Create(board, skipped);

        Assert.Equal(4, plan.SealsNeeded);
        Assert.Equal(2, plan.DoableOpen);
        Assert.Equal(2, plan.Deficit);
        Assert.Equal(2, plan.PointsReserved);
        Assert.True(plan.EnoughPoints);
        Assert.False(plan.RetryNow);
    }

    [Fact]
    public void Advisor_says_retry_when_only_skipped_objectives_remain()
    {
        var board = Board(0b11111, points: 3, openDoable: 11);
        var skipped = Enumerable.Range(5, 11).ToHashSet();
        var result = StickerAdvisor.Advise(board, OneLine.Value, skipped, DateTime.UtcNow);

        Assert.Equal(AdvisorAction.UseRetry, result.Action);
    }

    private static WtBoard Board(ushort stickers, byte points, int openDoable)
    {
        // Objectives with a seal are Claimed; the first `openDoable` remaining ones are Open.
        var status = new WtTaskStatus[16];
        var open = 0;
        for (var i = 0; i < 16; i++)
        {
            if ((stickers & (1 << i)) != 0)
                status[i] = WtTaskStatus.Claimed;
            else
                status[i] = open++ < openDoable ? WtTaskStatus.Open : WtTaskStatus.Claimed;
        }

        return new WtBoard
        {
            OrderIds = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray(),
            Status = status,
            StickerMask = stickers,
            SecondChancePoints = points,
            ExpiresUtc = DateTime.UtcNow.AddDays(7),
        };
    }

    private static ushort PlaceRandom(Random random, ushort mask)
    {
        var empty = Enumerable.Range(0, 16).Where(c => (mask & (1 << c)) == 0).ToArray();
        return (ushort)(mask | (1 << empty[random.Next(empty.Length)]));
    }

    private static ushort RandomMask(Random random, int seals)
    {
        var cells = Enumerable.Range(0, 16).OrderBy(_ => random.Next()).Take(seals);
        return (ushort)cells.Aggregate(0, (m, c) => m | (1 << c));
    }
}
