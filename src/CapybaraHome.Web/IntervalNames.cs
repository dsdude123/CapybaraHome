using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Web;

/// <summary>Turns an automation's repeat interval into the words the UI shows.</summary>
public static class IntervalNames
{
    /// <summary>Every unit, in the order the picker offers them.</summary>
    public static readonly TaskIntervalUnit[] All =
    [
        TaskIntervalUnit.Days,
        TaskIntervalUnit.Weeks,
        TaskIntervalUnit.Months,
        TaskIntervalUnit.Years,
    ];

    /// <summary>Returns the name of <paramref name="unit"/> as the picker lists it.</summary>
    public static string UnitLabel(TaskIntervalUnit unit) => unit switch
    {
        TaskIntervalUnit.Weeks => "weeks",
        TaskIntervalUnit.Months => "months",
        TaskIntervalUnit.Years => "years",
        _ => "days",
    };

    /// <summary>Returns the name of one of <paramref name="unit"/>, for an interval of exactly one.</summary>
    public static string SingularLabel(TaskIntervalUnit unit) => unit switch
    {
        TaskIntervalUnit.Weeks => "week",
        TaskIntervalUnit.Months => "month",
        TaskIntervalUnit.Years => "year",
        _ => "day",
    };

    /// <summary>Every day of the week, in the order the picker offers them.</summary>
    public static readonly DayOfWeek[] WeekDays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    /// <summary>
    /// Returns how often an automation runs, as the lists show it: "Every week" for an interval
    /// of one, "Every 13 days" for any other, and "Every 2 weeks on Thursday" when a weekly one
    /// is pinned to a day.
    /// </summary>
    /// <param name="unit">Unit the interval is counted in.</param>
    /// <param name="count">How many of that unit there are between runs.</param>
    /// <param name="weekDay">Day of the week a weekly schedule runs on, or null for none.</param>
    public static string Label(TaskIntervalUnit unit, int count, DayOfWeek? weekDay = null)
    {
        var every = count <= 1 ? $"Every {SingularLabel(unit)}" : $"Every {count} {UnitLabel(unit)}";
        return unit == TaskIntervalUnit.Weeks && weekDay is { } day ? $"{every} on {day}" : every;
    }
}
