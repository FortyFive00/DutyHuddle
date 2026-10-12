namespace DutyHuddle.Domain.Time;

/// <summary>FFXIV reset times: daily at 15:00 UTC, weekly on Tuesday at 08:00 UTC.</summary>
public static class GameClock
{
    public const int DailyResetHourUtc = 15;
    public const int WeeklyResetHourUtc = 8;

    /// <summary>The calendar date on which the current daily period started.</summary>
    public static DateOnly DailyEpoch(DateTime utc)
        => DateOnly.FromDateTime(EnsureUtc(utc).AddHours(-DailyResetHourUtc));

    public static DateTime NextDailyReset(DateTime utc)
        => DailyEpoch(utc).AddDays(1).ToDateTime(new TimeOnly(DailyResetHourUtc, 0), DateTimeKind.Utc);

    /// <summary>The Tuesday on which the current weekly period started.</summary>
    public static DateOnly WeeklyEpoch(DateTime utc)
    {
        var shifted = DateOnly.FromDateTime(EnsureUtc(utc).AddHours(-WeeklyResetHourUtc));
        var daysSinceTuesday = ((int)shifted.DayOfWeek - (int)DayOfWeek.Tuesday + 7) % 7;
        return shifted.AddDays(-daysSinceTuesday);
    }

    public static DateTime NextWeeklyReset(DateTime utc)
        => WeeklyEpoch(utc).AddDays(7).ToDateTime(new TimeOnly(WeeklyResetHourUtc, 0), DateTimeKind.Utc);

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
}
