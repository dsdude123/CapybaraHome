using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.Tasks;

/// <summary>
/// Works out a due date from a rule and the day the rule ran. Shared by anything that configures
/// a due date it cannot know the date of when it is saved.
/// </summary>
public static class TaskDueDate
{
    /// <summary>The largest <c>DueDateValue</c> each counted mode accepts.</summary>
    private static readonly Dictionary<TaskDueDateMode, int> Limits = new()
    {
        [TaskDueDateMode.InDays] = 3650,
        [TaskDueDateMode.InWeeks] = 520,
        [TaskDueDateMode.InMonths] = 120,
        [TaskDueDateMode.InYears] = 100,
    };

    /// <summary>Whether <paramref name="mode"/> needs a number of days, weeks, months or years.</summary>
    /// <param name="mode">Mode to ask about.</param>
    public static bool NeedsValue(TaskDueDateMode mode) => Limits.ContainsKey(mode);

    /// <summary>Whether <paramref name="mode"/> needs a date picked by hand.</summary>
    /// <param name="mode">Mode to ask about.</param>
    public static bool NeedsDate(TaskDueDateMode mode) => mode == TaskDueDateMode.OnDate;

    /// <summary>
    /// Checks that the rule carries what its mode needs, and returns the message to show when it
    /// does not. A counted mode is bounded so the arithmetic cannot run off the calendar.
    /// </summary>
    /// <param name="mode">How the due date is worked out.</param>
    /// <param name="value">Number of days, weeks, months or years, when the mode counts them.</param>
    /// <param name="onDate">Day picked by hand, when the mode uses one.</param>
    public static string? Validate(TaskDueDateMode mode, int? value, DateOnly? onDate)
    {
        if (NeedsDate(mode) && onDate is null)
        {
            return "Pick the date the new task is due.";
        }

        if (!NeedsValue(mode))
        {
            return null;
        }

        var limit = Limits[mode];
        return value is >= 1 && value <= limit
            ? null
            : $"The number of {UnitName(mode)} has to be between 1 and {limit}.";
    }

    /// <summary>
    /// Returns the due date the rule gives for a task completed on <paramref name="completedOn"/>,
    /// or null when the rule asks for no due date or is missing what its mode needs.
    /// </summary>
    /// <param name="mode">How the due date is worked out.</param>
    /// <param name="value">Number of days, weeks, months or years, when the mode counts them.</param>
    /// <param name="onDate">Day picked by hand, when the mode uses one.</param>
    /// <param name="completedOn">Day the relative modes are counted from.</param>
    public static DateOnly? Resolve(TaskDueDateMode mode, int? value, DateOnly? onDate, DateOnly completedOn)
    {
        if (NeedsValue(mode) && (value is null || Validate(mode, value, onDate) is not null))
        {
            return null;
        }

        return mode switch
        {
            TaskDueDateMode.OnDate => onDate,
            TaskDueDateMode.InDays => completedOn.AddDays(value!.Value),
            TaskDueDateMode.InWeeks => completedOn.AddDays(7 * value!.Value),
            TaskDueDateMode.InMonths => completedOn.AddMonths(value!.Value),
            TaskDueDateMode.InYears => completedOn.AddYears(value!.Value),
            TaskDueDateMode.NextQuarter => StartOfQuarter(completedOn).AddMonths(3),
            TaskDueDateMode.EndOfQuarter => StartOfQuarter(completedOn).AddMonths(3).AddDays(-1),
            _ => null,
        };
    }

    /// <summary>Returns the name of the unit <paramref name="mode"/> counts.</summary>
    /// <param name="mode">Mode to name the unit of.</param>
    public static string UnitName(TaskDueDateMode mode) => mode switch
    {
        TaskDueDateMode.InDays => "days",
        TaskDueDateMode.InWeeks => "weeks",
        TaskDueDateMode.InMonths => "months",
        TaskDueDateMode.InYears => "years",
        _ => "units",
    };

    private static DateOnly StartOfQuarter(DateOnly date) =>
        new(date.Year, (((date.Month - 1) / 3) * 3) + 1, 1);
}
