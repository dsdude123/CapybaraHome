using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Application.Tasks;

/// <summary>An automation as the API and the UI show it.</summary>
/// <param name="Id">Primary key.</param>
/// <param name="Title">Title given to the tasks it creates.</param>
/// <param name="Notes">Notes copied onto those tasks.</param>
/// <param name="CategoryId">Category they are filed under, or null.</param>
/// <param name="CategoryName">Name of that category, or null.</param>
/// <param name="Frequency">How often it runs.</param>
/// <param name="StartsOn">Day the interval is counted from.</param>
/// <param name="TimeOfDay">Time of day each run happens at, in the server's time zone.</param>
/// <param name="SkipWhenOpenTaskExists">Whether a run does nothing when a task with the same title is already open.</param>
/// <param name="IsEnabled">Whether it runs at all.</param>
/// <param name="LastRunAt">When it last ran, or null.</param>
/// <param name="LastRunSkipped">Whether that run skipped its task because one was already open.</param>
/// <param name="NextRunAt">When it runs next, or null when the schedule has no further occurrence.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public record TaskAutomationView(
    int Id,
    string Title,
    string? Notes,
    int? CategoryId,
    string? CategoryName,
    TaskAutomationFrequency Frequency,
    DateOnly StartsOn,
    TimeOnly TimeOfDay,
    bool SkipWhenOpenTaskExists,
    bool IsEnabled,
    DateTimeOffset? LastRunAt,
    bool LastRunSkipped,
    DateTimeOffset? NextRunAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Fields accepted when creating an automation.</summary>
/// <param name="Title">Title to give the tasks it creates; required.</param>
/// <param name="Notes">Notes to copy onto those tasks.</param>
/// <param name="Frequency">How often it runs.</param>
/// <param name="StartsOn">Day to count the interval from.</param>
/// <param name="TimeOfDay">Time of day to run at; midnight when left out.</param>
/// <param name="CategoryId">Category to file the tasks under, or null.</param>
/// <param name="SkipWhenOpenTaskExists">Whether to do nothing when a task with the same title is already open.</param>
/// <param name="IsEnabled">Whether it runs at all; true when left out.</param>
public record CreateTaskAutomationRequest(
    string Title,
    string? Notes = null,
    TaskAutomationFrequency Frequency = TaskAutomationFrequency.Weekly,
    DateOnly StartsOn = default,
    TimeOnly TimeOfDay = default,
    int? CategoryId = null,
    bool SkipWhenOpenTaskExists = false,
    bool IsEnabled = true);

/// <summary>Fields accepted when updating an automation. A null member leaves that field alone.</summary>
/// <param name="Title">New title.</param>
/// <param name="Notes">New notes; pass an empty string to clear them.</param>
/// <param name="Frequency">New frequency.</param>
/// <param name="StartsOn">New start date.</param>
/// <param name="TimeOfDay">New time of day.</param>
/// <param name="CategoryId">New category.</param>
/// <param name="ClearCategory">When true, removes the category.</param>
/// <param name="SkipWhenOpenTaskExists">New setting for the skip option.</param>
/// <param name="IsEnabled">New setting for whether it runs.</param>
public record UpdateTaskAutomationRequest(
    string? Title = null,
    string? Notes = null,
    TaskAutomationFrequency? Frequency = null,
    DateOnly? StartsOn = null,
    TimeOnly? TimeOfDay = null,
    int? CategoryId = null,
    bool ClearCategory = false,
    bool? SkipWhenOpenTaskExists = null,
    bool? IsEnabled = null);

/// <summary>What one sweep of the due automations did.</summary>
/// <param name="Ran">Automations whose run came due.</param>
/// <param name="TasksCreated">Tasks created by those runs.</param>
/// <param name="Skipped">Runs that created nothing because a task with the same title was already open.</param>
public record TaskAutomationRunSummary(int Ran, int TasksCreated, int Skipped);
