namespace CapybaraHome.Domain.Tasks;

/// <summary>
/// How a due date is worked out. Every relative mode is counted from the day the rule ran rather
/// than from a stored date, so a task closed late is followed by one due late as well.
/// </summary>
public enum TaskDueDateMode
{
    /// <summary>No due date.</summary>
    None,

    /// <summary>A date picked by hand.</summary>
    OnDate,

    /// <summary>A number of days later.</summary>
    InDays,

    /// <summary>A number of weeks later.</summary>
    InWeeks,

    /// <summary>A number of months later.</summary>
    InMonths,

    /// <summary>A number of years later.</summary>
    InYears,

    /// <summary>The first day of the following quarter.</summary>
    NextQuarter,

    /// <summary>The last day of the quarter it falls in.</summary>
    EndOfQuarter,
}
