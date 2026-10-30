using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Tasks;

/// <summary>Reads and writes the task automations, and runs the ones that have come due.</summary>
public class TaskAutomationService(ICapybaraHomeDbContext context, TaskService tasks, TimeProvider clock)
{
    /// <summary>How many upcoming executions the editor's preview shows.</summary>
    public const int PreviewCount = 5;

    /// <summary>The time zone the start dates and times of day are read in: the server's own.</summary>
    public TimeZoneInfo Zone => clock.LocalTimeZone;

    /// <summary>Returns every automation, by title.</summary>
    public async Task<IReadOnlyList<TaskAutomationView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.TaskAutomations.AsNoTracking()
            .Include(a => a.Category)
            .OrderBy(a => a.Title)
            .ThenBy(a => a.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(View).ToList();
    }

    /// <summary>Returns one automation, or null when none has that id.</summary>
    public async Task<TaskAutomationView?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await context.TaskAutomations.AsNoTracking()
            .Include(a => a.Category)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

        return row is null ? null : View(row);
    }

    /// <summary>Creates an automation and returns it as saved.</summary>
    /// <exception cref="ArgumentException">The title is blank, or the category does not exist.</exception>
    public async Task<TaskAutomationView> CreateAsync(CreateTaskAutomationRequest request, CancellationToken cancellationToken = default)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            throw new ArgumentException("An automation needs a title.", nameof(request));
        }

        EnsureDueInDaysIsSound(request.DueInDays);
        EnsureIntervalIsSound(request.IntervalCount);
        await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);

        var now = clock.GetUtcNow();
        var automation = new TaskAutomation
        {
            Title = title,
            Notes = Blank(request.Notes),
            CategoryId = request.CategoryId,
            IntervalUnit = request.IntervalUnit,
            IntervalCount = request.IntervalCount,
            StartsOn = request.StartsOn == default ? Today() : request.StartsOn,
            WeekDay = request.IntervalUnit == TaskIntervalUnit.Weeks ? request.WeekDay : null,
            TimeOfDay = request.TimeOfDay,
            DueInDays = request.DueInDays,
            SkipWhenOpenTaskExists = request.SkipWhenOpenTaskExists,
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            UpdatedAt = now,
        };

        context.TaskAutomations.Add(automation);
        await context.SaveChangesAsync(cancellationToken);

        return await GetAsync(automation.Id, cancellationToken)
            ?? throw new InvalidOperationException("The new automation could not be read back.");
    }

    /// <summary>Applies the set members of <paramref name="request"/>, or returns null when no automation has that id.</summary>
    /// <exception cref="ArgumentException">The title is blank, or the category does not exist.</exception>
    public async Task<TaskAutomationView?> UpdateAsync(int id, UpdateTaskAutomationRequest request, CancellationToken cancellationToken = default)
    {
        var automation = await context.TaskAutomations.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (automation is null)
        {
            return null;
        }

        if (request.Title is not null)
        {
            var title = request.Title.Trim();
            if (title.Length == 0)
            {
                throw new ArgumentException("An automation needs a title.", nameof(request));
            }

            automation.Title = title;
        }

        if (request.Notes is not null)
        {
            automation.Notes = Blank(request.Notes);
        }

        if (request.ClearCategory)
        {
            automation.CategoryId = null;
        }
        else if (request.CategoryId is not null)
        {
            await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);
            automation.CategoryId = request.CategoryId;
        }

        if (request.IntervalUnit is { } unit)
        {
            automation.IntervalUnit = unit;
        }

        if (request.IntervalCount is { } intervalCount)
        {
            EnsureIntervalIsSound(intervalCount);
            automation.IntervalCount = intervalCount;
        }

        if (request.StartsOn is { } startsOn)
        {
            automation.StartsOn = startsOn;
        }

        if (request.ClearWeekDay)
        {
            automation.WeekDay = null;
        }
        else if (request.WeekDay is { } weekDay)
        {
            automation.WeekDay = weekDay;
        }

        // A day of the week only means something while the interval is counted in weeks, so one
        // left behind by a change of unit goes rather than waiting to surprise a later change back.
        if (automation.IntervalUnit != TaskIntervalUnit.Weeks)
        {
            automation.WeekDay = null;
        }

        if (request.TimeOfDay is { } timeOfDay)
        {
            automation.TimeOfDay = timeOfDay;
        }

        if (request.ClearDueInDays)
        {
            automation.DueInDays = null;
        }
        else if (request.DueInDays is not null)
        {
            EnsureDueInDaysIsSound(request.DueInDays);
            automation.DueInDays = request.DueInDays;
        }

        if (request.SkipWhenOpenTaskExists is { } skip)
        {
            automation.SkipWhenOpenTaskExists = skip;
        }

        if (request.IsEnabled is { } enabled)
        {
            automation.IsEnabled = enabled;
        }

        automation.UpdatedAt = clock.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Deletes an automation. Returns false when no automation has that id.</summary>
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var automation = await context.TaskAutomations.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (automation is null)
        {
            return false;
        }

        context.TaskAutomations.Remove(automation);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Returns the next few executions a schedule would have, counted from now, which is what the
    /// editor shows before the automation is saved.
    /// </summary>
    /// <param name="unit">Unit the interval would be counted in.</param>
    /// <param name="count">How many of that unit there would be between runs.</param>
    /// <param name="startsOn">Day the interval would be counted from.</param>
    /// <param name="weekDay">Day of the week a weekly schedule would run on, or null for the start date's own day.</param>
    /// <param name="timeOfDay">Time of day it would run at.</param>
    /// <param name="after">Moment to count from, or null for now.</param>
    public IReadOnlyList<DateTimeOffset> Preview(
        TaskIntervalUnit unit,
        int count,
        DateOnly startsOn,
        DayOfWeek? weekDay,
        TimeOnly timeOfDay,
        DateTimeOffset? after = null)
    {
        return TaskAutomationSchedule.Upcoming(
            unit,
            count,
            TaskAutomationSchedule.Anchor(unit, startsOn, weekDay),
            timeOfDay,
            after ?? clock.GetUtcNow(),
            PreviewCount,
            Zone);
    }

    /// <summary>
    /// Runs every enabled automation whose next execution has passed. A run that was missed while
    /// the app was down happens once on the next sweep rather than once per execution it slept
    /// through, because the point of the schedule is one open task, not a backlog of them.
    /// </summary>
    public async Task<TaskAutomationRunSummary> RunDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var due = await context.TaskAutomations
            .Where(a => a.IsEnabled)
            .ToListAsync(cancellationToken);

        var ran = 0;
        var created = 0;
        var skipped = 0;

        foreach (var automation in due)
        {
            var next = NextRunAt(automation);
            if (next is null || next > now)
            {
                continue;
            }

            ran++;

            if (automation.SkipWhenOpenTaskExists && await HasOpenTaskAsync(automation.Title, cancellationToken))
            {
                automation.LastRunSkipped = true;
                skipped++;
            }
            else
            {
                await tasks.CreateAsync(
                    new CreateTaskRequest(
                        automation.Title,
                        automation.Notes,
                        TaskItemStatus.ToDo,
                        automation.CategoryId,
                        DueDate(automation, now)),
                    cancellationToken);

                automation.LastRunSkipped = false;
                created++;
            }

            automation.LastRunAt = now;
            automation.UpdatedAt = now;
        }

        if (ran > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return new TaskAutomationRunSummary(ran, created, skipped);
    }

    /// <summary>
    /// Whether a task with this title is open. The comparison is lowered rather than left to the
    /// column's collation, which SQLite makes case-sensitive, because the point of the option is
    /// that a task he already has counts however he capitalised it.
    /// </summary>
    private Task<bool> HasOpenTaskAsync(string title, CancellationToken cancellationToken)
    {
        var lowered = title.ToLowerInvariant();
        return context.Tasks.AnyAsync(
            t => t.Status != TaskItemStatus.Done && t.Title.ToLower() == lowered,
            cancellationToken);
    }

    /// <summary>
    /// When <paramref name="automation"/> runs next: the first execution after its last run, or
    /// after it was created while it has never run, so that an interval counted from a start date
    /// in the past keeps the start date's phase without firing for every month it missed.
    /// </summary>
    private DateTimeOffset? NextRunAt(TaskAutomation automation) => TaskAutomationSchedule.NextAfter(
        automation.IntervalUnit,
        automation.IntervalCount,
        TaskAutomationSchedule.Anchor(automation.IntervalUnit, automation.StartsOn, automation.WeekDay),
        automation.TimeOfDay,
        automation.LastRunAt ?? automation.CreatedAt,
        Zone);

    /// <summary>
    /// The due date to give a task created at <paramref name="createdAt"/>, counted in days from
    /// the day it is created where the server is, or null when the automation sets no due date.
    /// </summary>
    private DateOnly? DueDate(TaskAutomation automation, DateTimeOffset createdAt) => automation.DueInDays is { } days
        ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(createdAt, Zone).DateTime).AddDays(days)
        : null;

    private static void EnsureIntervalIsSound(int count)
    {
        if (count < TaskAutomationSchedule.MinimumIntervalCount)
        {
            throw new ArgumentException("An automation has to repeat at least every one unit.", nameof(count));
        }
    }

    private static void EnsureDueInDaysIsSound(int? dueInDays)
    {
        if (dueInDays is < 0)
        {
            throw new ArgumentException("A task cannot be due before it is created.", nameof(dueInDays));
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);

    private async Task EnsureCategoryExistsAsync(int? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return;
        }

        var exists = await context.Categories.AnyAsync(c => c.Id == categoryId, cancellationToken);
        if (!exists)
        {
            throw new ArgumentException($"No category has id {categoryId}.", nameof(categoryId));
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private TaskAutomationView View(TaskAutomation automation) => new(
        automation.Id,
        automation.Title,
        automation.Notes,
        automation.CategoryId,
        automation.Category?.Name,
        automation.IntervalUnit,
        automation.IntervalCount,
        automation.StartsOn,
        automation.WeekDay,
        automation.TimeOfDay,
        automation.DueInDays,
        automation.SkipWhenOpenTaskExists,
        automation.IsEnabled,
        automation.LastRunAt,
        automation.LastRunSkipped,
        NextRunAt(automation),
        automation.CreatedAt,
        automation.UpdatedAt);
}
