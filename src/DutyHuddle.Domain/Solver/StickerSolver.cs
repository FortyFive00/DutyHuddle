using System.Numerics;

namespace DutyHuddle.Domain.Solver;

public enum AdvisorGoal
{
    AtLeastOneLine = 0,
    AtLeastTwoLines = 1,
    ThreeLines = 2,
    WeightedReward = 3,
}

/// <summary>Relative value of finishing with 1, 2 or 3 lines, used by <see cref="AdvisorGoal.WeightedReward"/>.</summary>
public readonly record struct LineWeights(double One, double Two, double Three)
{
    public static LineWeights Default => new(1, 3, 8);
}

/// <summary>Probability of finishing the journal with at least 1, at least 2, and exactly 3 lines.</summary>
public readonly record struct LineOdds(double AtLeastOne, double AtLeastTwo, double Three)
{
    public static LineOdds FromDistribution(ReadOnlySpan<float> d) => new(d[1] + d[2] + d[3], d[2] + d[3], d[3]);
}

public sealed record SolverEvaluation(
    double OptimalValue,
    LineOdds Optimal,
    LineOdds PlaceNext,
    LineOdds? ShuffleNow,
    double PlaceValue,
    double? ShuffleValue,
    bool ShuffleIsBest);

/// <summary>
/// Exact dynamic programming over every grid with at most 9 seals and 0-9 Second Chance points.
/// Model (Console Games Wiki, VanillaPlus solver): a new seal lands on a uniformly random empty cell;
/// Shuffle costs 2 points, needs 3-7 seals and redistributes all seals uniformly at random.
/// The value function is maximised for the chosen goal, and the line distribution under that
/// optimal policy is propagated alongside it so the UI can show real probabilities.
/// </summary>
public sealed class StickerSolver
{
    public const int MaxPoints = 9;
    public const int SealsToComplete = 9;
    public const int ShuffleCost = 2;
    public const int MinSealsForShuffle = 3;
    public const int MaxSealsForShuffle = 7;

    private const int CellCount = 16;
    private const int PointLevels = MaxPoints + 1;
    private const int SizeLevels = SealsToComplete + 1;

    private static readonly ushort[][] MasksBySize = BuildMasksBySize();
    private static readonly int[] CompactIndex = BuildCompactIndex();
    private static readonly int StateCount = MasksBySize.Sum(m => m.Length);

    private readonly double[] value;
    private readonly float[] distribution;
    private readonly double[] shuffleValue;
    private readonly float[] shuffleDistribution;

    public StickerSolver(AdvisorGoal goal, LineWeights? weights = null)
    {
        Goal = goal;
        Weights = weights ?? LineWeights.Default;
        value = new double[PointLevels * StateCount];
        distribution = new float[PointLevels * StateCount * 4];
        shuffleValue = new double[PointLevels * SizeLevels];
        shuffleDistribution = new float[PointLevels * SizeLevels * 4];
        Compute();
    }

    public AdvisorGoal Goal { get; }

    public LineWeights Weights { get; }

    public static bool CanShuffle(int seals, int points)
        => seals is >= MinSealsForShuffle and <= MaxSealsForShuffle && points >= ShuffleCost;

    /// <summary>Utility of a finished journal with the given number of lines.</summary>
    public double Utility(int lines) => Goal switch
    {
        AdvisorGoal.AtLeastOneLine => lines >= 1 ? 1 : 0,
        AdvisorGoal.AtLeastTwoLines => lines >= 2 ? 1 : 0,
        AdvisorGoal.ThreeLines => lines >= 3 ? 1 : 0,
        _ => lines switch { 0 => 0, 1 => Weights.One, 2 => Weights.Two, _ => Weights.Three },
    };

    public SolverEvaluation Evaluate(ushort mask, int points)
    {
        points = Math.Clamp(points, 0, MaxPoints);
        var seals = BitOperations.PopCount(mask);
        if (seals > SealsToComplete)
            throw new ArgumentOutOfRangeException(nameof(mask), "A journal holds at most 9 seals.");

        var state = Slot(points, mask);
        var optimal = LineOdds.FromDistribution(distribution.AsSpan(state * 4, 4));

        if (seals == SealsToComplete)
            return new SolverEvaluation(value[state], optimal, optimal, null, value[state], null, false);

        Span<float> place = stackalloc float[4];
        var placeValue = PlaceAverage(points, mask, place);

        double? shuffle = null;
        LineOdds? shuffleOdds = null;
        if (CanShuffle(seals, points))
        {
            var s = ShuffleSlot(points - ShuffleCost, seals);
            shuffle = shuffleValue[s];
            shuffleOdds = LineOdds.FromDistribution(shuffleDistribution.AsSpan(s * 4, 4));
        }

        var shuffleIsBest = shuffle is { } sv && sv > placeValue + 1e-12;
        return new SolverEvaluation(value[state], optimal, LineOdds.FromDistribution(place), shuffleOdds, placeValue, shuffle, shuffleIsBest);
    }

    private void Compute()
    {
        Span<float> place = stackalloc float[4];
        Span<double> acc = stackalloc double[4];
        for (var p = 0; p < PointLevels; p++)
        {
            for (var n = SealsToComplete; n >= 0; n--)
            {
                foreach (var mask in MasksBySize[n])
                {
                    var slot = Slot(p, mask);
                    var dist = distribution.AsSpan(slot * 4, 4);

                    if (n == SealsToComplete)
                    {
                        var lines = Math.Min(3, LineTable.CountLines(mask));
                        value[slot] = Utility(lines);
                        dist.Clear();
                        dist[lines] = 1f;
                        continue;
                    }

                    var placeValue = PlaceAverage(p, mask, place);
                    if (CanShuffle(n, p) && shuffleValue[ShuffleSlot(p - ShuffleCost, n)] > placeValue + 1e-12)
                    {
                        var s = ShuffleSlot(p - ShuffleCost, n);
                        value[slot] = shuffleValue[s];
                        shuffleDistribution.AsSpan(s * 4, 4).CopyTo(dist);
                    }
                    else
                    {
                        value[slot] = placeValue;
                        place.CopyTo(dist);
                    }
                }

                // Average over every grid of this size: the outcome of a Shuffle that leaves p points.
                var masks = MasksBySize[n];
                double sum = 0;
                acc.Clear();
                foreach (var mask in masks)
                {
                    var slot = Slot(p, mask);
                    sum += value[slot];
                    for (var i = 0; i < 4; i++)
                        acc[i] += distribution[slot * 4 + i];
                }

                var target = ShuffleSlot(p, n);
                shuffleValue[target] = sum / masks.Length;
                for (var i = 0; i < 4; i++)
                    shuffleDistribution[target * 4 + i] = (float)(acc[i] / masks.Length);
            }
        }
    }

    /// <summary>Expected value (and line distribution) when the next seal lands on a random empty cell.</summary>
    private double PlaceAverage(int points, ushort mask, Span<float> result)
    {
        double total = 0;
        Span<double> acc = stackalloc double[4];
        acc.Clear();
        var empty = 0;
        for (var cell = 0; cell < CellCount; cell++)
        {
            if ((mask & (1 << cell)) != 0)
                continue;

            var next = Slot(points, (ushort)(mask | (1 << cell)));
            total += value[next];
            for (var i = 0; i < 4; i++)
                acc[i] += distribution[next * 4 + i];
            empty++;
        }

        for (var i = 0; i < 4; i++)
            result[i] = (float)(acc[i] / empty);
        return total / empty;
    }

    private static int Slot(int points, ushort mask) => points * StateCount + CompactIndex[mask];

    private static int ShuffleSlot(int points, int seals) => points * SizeLevels + seals;

    private static ushort[][] BuildMasksBySize()
    {
        var buckets = Enumerable.Range(0, SizeLevels).Select(_ => new List<ushort>()).ToArray();
        for (var mask = 0; mask < 1 << CellCount; mask++)
        {
            var n = BitOperations.PopCount((uint)mask);
            if (n <= SealsToComplete)
                buckets[n].Add((ushort)mask);
        }

        return [.. buckets.Select(b => b.ToArray())];
    }

    private static int[] BuildCompactIndex()
    {
        var index = new int[1 << CellCount];
        Array.Fill(index, -1);
        var next = 0;
        foreach (var bucket in MasksBySize)
            foreach (var mask in bucket)
                index[mask] = next++;
        return index;
    }
}
