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
        TaskIntervalUnit.Days => "days",
        TaskIntervalUnit.Weeks => "weeks",
        TaskIntervalUnit.Months => "months",
        TaskIntervalUnit.Years => "years",
        _ => unit.ToString().ToLowerInvariant(),
    };

    /// <summary>Returns the name of one of <paramref name="unit"/>, for an interval of exactly one.</summary>
    public static string SingularLabel(TaskIntervalUnit unit) => unit switch
    {
        TaskIntervalUnit.Days => "day",
        TaskIntervalUnit.Weeks => "week",
        TaskIntervalUnit.Months => "month",
        TaskIntervalUnit.Years => "year",
        _ => UnitLabel(unit),
    };

    /// <summary>
    /// Returns how often an automation runs, as the lists show it: "Every week" for an interval
    /// of one, and "Every 13 days" for any other.
    /// </summary>
    /// <param name="unit">Unit the interval is counted in.</param>
    /// <param name="count">How many of that unit there are between runs.</param>
    public static string Label(TaskIntervalUnit unit, int count) =>
        count <= 1 ? $"Every {SingularLabel(unit)}" : $"Every {count} {UnitLabel(unit)}";
}
