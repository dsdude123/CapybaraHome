namespace CapybaraHome.Domain.Tasks;

/// <summary>
/// A rule that creates a task on a timed basis. The interval is any number of days, weeks, months
/// or years; the start date is what it is counted from, and the time of day is when the run
/// happens on each of those days.
/// </summary>
public class TaskAutomation
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Title given to every task this automation creates, and the name it is listed under.</summary>
    public required string Title { get; set; }

    /// <summary>Notes copied onto every task this automation creates, held as markdown.</summary>
    public string? Notes { get; set; }

    /// <summary>Category the created tasks are filed under, or null for none.</summary>
    public int? CategoryId { get; set; }

    /// <summary>Navigation to <see cref="CategoryId"/>.</summary>
    public Category? Category { get; set; }

    /// <summary>The unit the repeat interval is counted in.</summary>
    public TaskIntervalUnit IntervalUnit { get; set; } = TaskIntervalUnit.Weeks;

    /// <summary>How many of <see cref="IntervalUnit"/> there are between runs. At least one.</summary>
    public int IntervalCount { get; set; } = 1;

    /// <summary>Day the interval is counted from. The first run is on this day.</summary>
    public DateOnly StartsOn { get; set; }

    /// <summary>
    /// Day of the week a weekly schedule runs on, or null to run on whichever day
    /// <see cref="StartsOn"/> falls on. Only weeks are counted in days of the week, so it is null
    /// on every other unit.
    /// </summary>
    public DayOfWeek? WeekDay { get; set; }

    /// <summary>Time of day each run happens at, in the server's time zone. Midnight unless changed.</summary>
    public TimeOnly TimeOfDay { get; set; }

    /// <summary>
    /// How many days after it is created the task is due, or null to leave it with no due date.
    /// Zero makes it due the day it is created.
    /// </summary>
    public int? DueInDays { get; set; }

    /// <summary>Whether a run does nothing when a task with the same title is already open.</summary>
    public bool SkipWhenOpenTaskExists { get; set; }

    /// <summary>Whether the automation runs at all. A paused one keeps its schedule.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>When the automation last ran, or null while it never has.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>Whether that last run skipped its task because one was already open.</summary>
    public bool LastRunSkipped { get; set; }

    /// <summary>When the row was created, in UTC. The schedule is counted from here until the first run.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
