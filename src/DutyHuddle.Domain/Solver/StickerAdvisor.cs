using DutyHuddle.Domain.Models;

namespace DutyHuddle.Domain.Solver;

public enum AdvisorAction
{
    NoJournal,
    JournalExpired,
    CompleteBook,
    Continue,
    UseShuffle,
    UseRetry,
    DontShuffle,
}

/// <summary>
/// How many Retries the journal needs. Retry (1 point) reopens a completed objective of your choice and
/// marks a random open objective complete without a seal. Used after every doable objective is done,
/// the random target is always an objective you could not do, so each Retry is worth exactly one seal.
/// </summary>
public sealed record RetryPlan(int SealsNeeded, int DoableOpen, int Deficit, int PointsReserved, bool EnoughPoints, bool RetryNow)
{
    public const int RetryCost = 1;

    public static RetryPlan Create(WtBoard board, IReadOnlySet<int> skippedObjectives)
    {
        var needed = Math.Max(0, WtBoard.SealsToComplete - board.PlacedSeals - board.ClaimableCount);
        var doable = Enumerable.Range(0, WtBoard.CellCount)
            .Count(i => board.Status[i] == WtTaskStatus.Open && !skippedObjectives.Contains(i));
        var deficit = Math.Max(0, needed - doable);
        var points = board.SecondChancePoints;
        var reserved = Math.Min(points, deficit * RetryCost);
        var hasCompleted = board.Status.Any(s => s != WtTaskStatus.Open);
        var retryNow = deficit > 0 && doable == 0 && points >= RetryCost && hasCompleted;
        return new RetryPlan(needed, doable, deficit, reserved, points >= deficit * RetryCost, retryNow);
    }
}

public sealed record AdvisorResult(
    AdvisorAction Action,
    string Headline,
    string Detail,
    SolverEvaluation? Evaluation,
    RetryPlan? Retry,
    int PointsForShuffle,
    IReadOnlyList<string> Warnings);

/// <summary>Combines the exact solver with the Retry plan and turns the numbers into one recommendation.</summary>
public static class StickerAdvisor
{
    /// <summary>Shuffle must beat continuing by at least this much (in goal units) to be recommended.</summary>
    public const double ShuffleMargin = 0.005;

    public static AdvisorResult Advise(WtBoard? board, StickerSolver solver, IReadOnlySet<int> skippedObjectives, DateTime nowUtc)
    {
        if (board is null)
            return Simple(AdvisorAction.NoJournal, "No journal", "Pick up a Wondrous Tails journal from Khloe Aliapoh in Idyllshire.");

        if (board.IsExpired(nowUtc))
            return Simple(AdvisorAction.JournalExpired, "Journal expired", "Hand it to Khloe to receive a new one.");

        if (board.IsComplete)
            return Simple(AdvisorAction.CompleteBook, "Turn in the journal",
                $"All 9 seals are placed ({LineTable.CountLines(board.StickerMask)} line(s)). Turn the journal in to Khloe.");

        var retry = RetryPlan.Create(board, skippedObjectives);
        var pointsForShuffle = Math.Clamp(board.SecondChancePoints - retry.PointsReserved, 0, StickerSolver.MaxPoints);
        var evaluation = solver.Evaluate(board.StickerMask, pointsForShuffle);
        var warnings = new List<string>();

        if (retry.Deficit > 0 && !retry.EnoughPoints)
            warnings.Add($"You need {retry.Deficit} Retry but have {board.SecondChancePoints} point(s). Run duties with first-timers to earn more.");
        else if (retry.Deficit > 0 && !retry.RetryNow)
            warnings.Add($"{retry.PointsReserved} point(s) reserved for {retry.Deficit} Retry after your doable objectives are done.");

        if (retry.RetryNow)
        {
            return new AdvisorResult(AdvisorAction.UseRetry, "Use Retry now",
                "Pick a completed objective you can repeat quickly. All open objectives are ones you skip, so the Retry is a guaranteed extra seal.",
                evaluation, retry, pointsForShuffle, warnings);
        }

        var seals = board.PlacedSeals;
        if (StickerSolver.CanShuffle(seals, pointsForShuffle))
        {
            if (evaluation.ShuffleValue is { } shuffle && shuffle > evaluation.PlaceValue + ShuffleMargin)
            {
                return new AdvisorResult(AdvisorAction.UseShuffle, "Shuffle now",
                    Describe("Shuffling now", evaluation.ShuffleNow!.Value, "continuing", evaluation.PlaceNext, solver.Goal),
                    evaluation, retry, pointsForShuffle, warnings);
            }

            return new AdvisorResult(AdvisorAction.DontShuffle, "Don't Shuffle, keep going",
                "Shuffling would not improve your odds: your layout is at least as good as a random one. Keep your points and keep clearing objectives.",
                evaluation, retry, pointsForShuffle, warnings);
        }

        var detail = board.ClaimableCount > 0
            ? $"Affix your {board.ClaimableCount} claimable seal(s), then keep clearing objectives."
            : $"Clear {retry.SealsNeeded} more objective(s) to finish the journal.";
        return new AdvisorResult(AdvisorAction.Continue, "Keep going", detail, evaluation, retry, pointsForShuffle, warnings);
    }

    /// <summary>Percentage with a dot as decimal separator, whatever the system culture (the UI is English).</summary>
    public static string Percent(double probability, int decimals = 1)
        => (probability * 100).ToString(decimals == 2 ? "0.00" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    public static double GoalProbability(LineOdds odds, AdvisorGoal goal) => goal switch
    {
        AdvisorGoal.AtLeastTwoLines => odds.AtLeastTwo,
        AdvisorGoal.ThreeLines => odds.Three,
        _ => odds.AtLeastOne,
    };

    private static string Describe(string action, LineOdds a, string other, LineOdds b, AdvisorGoal goal)
    {
        var label = goal switch
        {
            AdvisorGoal.AtLeastTwoLines => "at least 2 lines",
            AdvisorGoal.ThreeLines => "3 lines",
            _ => "at least 1 line",
        };
        return $"{action} gives {Percent(GoalProbability(a, goal))} for {label}, versus {Percent(GoalProbability(b, goal))} by {other}.";
    }

    private static AdvisorResult Simple(AdvisorAction action, string headline, string detail)
        => new(action, headline, detail, null, null, 0, []);
}
