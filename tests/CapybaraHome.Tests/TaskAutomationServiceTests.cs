using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>Covers what an automation generates, when it generates it, and when it stands down.</summary>
public class TaskAutomationServiceTests
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    [Fact]
    public async Task A_new_automation_starts_today_at_midnight_in_the_servers_zone()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        var created = await service.CreateAsync(new CreateTaskAutomationRequest("  Take the bins out  "));

        Assert.Equal("Take the bins out", created.Title);
        Assert.Equal(TaskIntervalUnit.Weeks, created.IntervalUnit);
        Assert.Equal(1, created.IntervalCount);
        Assert.Equal(TimeOnly.MinValue, created.TimeOfDay);
        Assert.True(created.IsEnabled);

        // The clock sits at 09:00 UTC on the 18th, which is 02:00 that morning in Pacific, so
        // today's midnight execution has already gone and the weekly default lands a week on.
        Assert.Equal(new DateOnly(2026, 10, 18), created.StartsOn);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 7, 0, 0, TimeSpan.Zero), created.NextRunAt!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task A_blank_title_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateTaskAutomationRequest("   ")));
    }

    [Fact]
    public async Task A_category_that_does_not_exist_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(new CreateTaskAutomationRequest("Anything", CategoryId: 404)));
    }

    [Fact]
    public async Task A_run_that_has_come_due_creates_the_task_it_describes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var categories = new CategoryService(db.Context);
        var category = await categories.CreateAsync(new CategoryRequest("Garden"));

        await service.CreateAsync(new CreateTaskAutomationRequest(
            "Water the tomatoes",
            "The ones in the greenhouse",
            TaskIntervalUnit.Days,
            1,
            new DateOnly(2026, 10, 18),
            new TimeOnly(7, 0),
            category.Id));

        db.Clock.Advance(TimeSpan.FromDays(1));
        var summary = await service.RunDueAsync();

        Assert.Equal(new TaskAutomationRunSummary(1, 1, 0), summary);

        var task = await db.Context.Tasks.SingleAsync();
        Assert.Equal("Water the tomatoes", task.Title);
        Assert.Equal("The ones in the greenhouse", task.Notes);
        Assert.Equal(category.Id, task.CategoryId);
        Assert.Equal(TaskItemStatus.ToDo, task.Status);

        // Nothing asked for a due date, so the task has none.
        Assert.Null(task.DueDate);
    }

    [Theory]
    [InlineData(0, "2026-10-19")]
    [InlineData(1, "2026-10-20")]
    [InlineData(14, "2026-11-02")]
    public async Task A_due_date_is_counted_in_days_from_the_day_the_task_is_created(int dueInDays, string expected)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await service.CreateAsync(new CreateTaskAutomationRequest(
            "Pay the thing",
            IntervalUnit: TaskIntervalUnit.Days, IntervalCount: 1,
            StartsOn: new DateOnly(2026, 10, 18),
            TimeOfDay: new TimeOnly(9, 0),
            DueInDays: dueInDays));

        // The run lands on the 19th at 09:00 Pacific, which is the day the due date counts from.
        db.Clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(new TaskAutomationRunSummary(1, 1, 0), await service.RunDueAsync());

        Assert.Equal(DateOnly.Parse(expected), (await db.Context.Tasks.SingleAsync()).DueDate);
    }

    [Fact]
    public async Task A_due_date_counts_from_the_catch_up_run_rather_than_from_the_execution_it_missed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await service.CreateAsync(new CreateTaskAutomationRequest(
            "Daily thing",
            IntervalUnit: TaskIntervalUnit.Days, IntervalCount: 1,
            StartsOn: new DateOnly(2026, 10, 18),
            DueInDays: 3));

        // A month of downtime: the task is made today, so it is due three days from today.
        db.Clock.Advance(TimeSpan.FromDays(30));
        await service.RunDueAsync();

        Assert.Equal(new DateOnly(2026, 11, 20), (await db.Context.Tasks.SingleAsync()).DueDate);
    }

    [Fact]
    public async Task A_due_date_before_the_task_is_created_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(new CreateTaskAutomationRequest("Backwards", DueInDays: -1)));

        var created = await service.CreateAsync(new CreateTaskAutomationRequest("Forwards", DueInDays: 2));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(created.Id, new UpdateTaskAutomationRequest(DueInDays: -1)));
    }

    [Fact]
    public async Task A_due_date_is_added_and_taken_off_again()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var created = await service.CreateAsync(new CreateTaskAutomationRequest(
            "Changeable",
            IntervalUnit: TaskIntervalUnit.Days, IntervalCount: 1,
            StartsOn: new DateOnly(2026, 10, 18)));

        Assert.Null(created.DueInDays);

        var dated = await service.UpdateAsync(created.Id, new UpdateTaskAutomationRequest(DueInDays: 7));
        Assert.Equal(7, dated!.DueInDays);

        var undated = await service.UpdateAsync(created.Id, new UpdateTaskAutomationRequest(ClearDueInDays: true));
        Assert.Null(undated!.DueInDays);

        db.Clock.Advance(TimeSpan.FromDays(1));
        await service.RunDueAsync();
        Assert.Null((await db.Context.Tasks.SingleAsync()).DueDate);
    }

    [Fact]
    public async Task A_run_that_has_not_come_due_does_nothing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await service.CreateAsync(new CreateTaskAutomationRequest(
            "Not yet",
            IntervalUnit: TaskIntervalUnit.Months, IntervalCount: 1,
            StartsOn: new DateOnly(2026, 10, 18)));

        Assert.Equal(new TaskAutomationRunSummary(0, 0, 0), await service.RunDueAsync());
        Assert.Empty(db.Context.Tasks);
    }

    [Fact]
    public async Task A_paused_automation_never_runs_and_shows_no_next_execution()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        var created = await service.CreateAsync(new CreateTaskAutomationRequest(
            "Paused",
            IntervalUnit: TaskIntervalUnit.Days, IntervalCount: 1,
            StartsOn: new DateOnly(2026, 10, 1),
            IsEnabled: false));

        db.Clock.Advance(TimeSpan.FromDays(10));

        Assert.Equal(new TaskAutomationRunSummary(0, 0, 0), await service.RunDueAsync());
        Assert.Empty(db.Context.Tasks);

        var resumed = await service.UpdateAsync(created.Id, new UpdateTaskAutomationRequest(IsEnabled: true));
        Assert.Equal(new TaskAutomationRunSummary(1, 1, 0), await service.RunDueAsync());
        Assert.True(resumed!.IsEnabled);
    }

    [Fact]
    public async Task The_skip_option_stands_down_when_a_task_with_the_same_title_is_still_open()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var tasks = db.Tasks();

        await service.CreateAsync(new CreateTaskAutomationRequest(
            "Clean the gutters",
            IntervalUnit: TaskIntervalUnit.Days, IntervalCount: 1,
            StartsOn: new DateOnly(2026, 10, 18),
            SkipWhenOpenTaskExists: true));

        // Capitalised differently, because the point of the option is the task he already has.
        var open = await tasks.CreateAsync(new CreateTaskRequest("clean the GUTTERS"));

        db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(new TaskAutomationRunSummary(1, 0, 1), await service.RunDueAsync());
        Assert.Single(db.Context.Tasks);

        var skipped = Assert.Single(await service.ListAsync());
        Assert.True(skipped.LastRunSkipped);

        // Once it is done it no longer counts, so the next run creates the task.
        await tasks.UpdateAsync(open.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        db.Clock.Advance(TimeSpan.FromDays(1));

        Assert.Equal(new TaskAutomationRunSummary(1, 1, 0), await service.RunDueAsync());
        Assert.Equal(2, await db.Context.Tasks.CountAsync());
        Assert.False(Assert.Single(await service.ListAsync()).LastRunSkipped);
    }

    [Fact]
    public async Task Without_the_skip_option_a_second_task_is_created_alongside_the_open_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var tasks = db.Tasks();

        await service.CreateAsync(new CreateTaskAutomationRequest(
            "Hoover upstairs",
            IntervalUnit: TaskIntervalUnit.Days, IntervalCount: 1,
            StartsOn: new DateOnly(2026, 10, 18)));

        await tasks.CreateAsync(new CreateTaskRequest("Hoover upstairs"));

        db.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(new TaskAutomationRunSummary(1, 1, 0), await service.RunDueAsync());
        Assert.Equal(2, await db.Context.Tasks.CountAsync());
    }

    [Fact]
    public async Task Executions_missed_while_the_app_was_down_produce_one_task_rather_than_a_backlog()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await service.CreateAsync(new CreateTaskAutomationRequest(
            "Daily thing",
            IntervalUnit: TaskIntervalUnit.Days, IntervalCount: 1,
            StartsOn: new DateOnly(2026, 10, 18)));

        db.Clock.Advance(TimeSpan.FromDays(30));
        Assert.Equal(new TaskAutomationRunSummary(1, 1, 0), await service.RunDueAsync());
        Assert.Single(db.Context.Tasks);

        // The next execution is counted from the run that just happened.
        Assert.Equal(new TaskAutomationRunSummary(0, 0, 0), await service.RunDueAsync());
        Assert.Single(db.Context.Tasks);
    }

    [Fact]
    public async Task A_start_date_in_the_past_keeps_its_phase_rather_than_firing_at_once()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        // Counted fortnightly from the 1st, the executions are the 1st, 15th and 29th.
        var created = await service.CreateAsync(new CreateTaskAutomationRequest(
            "Bin day",
            IntervalUnit: TaskIntervalUnit.Weeks, IntervalCount: 2,
            StartsOn: new DateOnly(2026, 10, 1)));

        Assert.Equal(new DateOnly(2026, 10, 29), Local(created.NextRunAt!.Value));
        Assert.Equal(new TaskAutomationRunSummary(0, 0, 0), await service.RunDueAsync());
    }

    [Fact]
    public async Task Editing_an_automation_moves_its_schedule_and_can_clear_its_category()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var categories = new CategoryService(db.Context);
        var category = await categories.CreateAsync(new CategoryRequest("Garden"));

        var created = await service.CreateAsync(new CreateTaskAutomationRequest("Something", CategoryId: category.Id));

        var updated = await service.UpdateAsync(created.Id, new UpdateTaskAutomationRequest(
            Title: " Something else ",
            Notes: "with notes",
            IntervalUnit: TaskIntervalUnit.Months, IntervalCount: 3,
            StartsOn: new DateOnly(2027, 1, 15),
            TimeOfDay: new TimeOnly(18, 30),
            ClearCategory: true,
            SkipWhenOpenTaskExists: true));

        Assert.Equal("Something else", updated!.Title);
        Assert.Equal("with notes", updated.Notes);
        Assert.Equal(TaskIntervalUnit.Months, updated.IntervalUnit);
        Assert.Equal(3, updated.IntervalCount);
        Assert.Equal(new DateOnly(2027, 1, 15), updated.StartsOn);
        Assert.Equal(new TimeOnly(18, 30), updated.TimeOfDay);
        Assert.Null(updated.CategoryId);
        Assert.True(updated.SkipWhenOpenTaskExists);
        Assert.Equal(new DateOnly(2027, 1, 15), Local(updated.NextRunAt!.Value));
    }

    [Fact]
    public async Task An_automation_is_deleted_and_an_unknown_id_is_reported_rather_than_thrown()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);
        var created = await service.CreateAsync(new CreateTaskAutomationRequest("Short lived"));

        Assert.True(await service.DeleteAsync(created.Id));
        Assert.False(await service.DeleteAsync(created.Id));
        Assert.Null(await service.GetAsync(created.Id));
        Assert.Empty(await service.ListAsync());
    }

    [Theory]
    [InlineData(TaskIntervalUnit.Days, 1)]
    [InlineData(TaskIntervalUnit.Days, 13)]
    [InlineData(TaskIntervalUnit.Weeks, 1)]
    [InlineData(TaskIntervalUnit.Weeks, 2)]
    [InlineData(TaskIntervalUnit.Months, 1)]
    [InlineData(TaskIntervalUnit.Months, 9)]
    [InlineData(TaskIntervalUnit.Years, 1)]
    [InlineData(TaskIntervalUnit.Years, 3)]
    public async Task Every_interval_previews_and_runs(TaskIntervalUnit unit, int count)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        var preview = service.Preview(unit, count, new DateOnly(2026, 10, 20), new TimeOnly(6, 0));
        Assert.Equal(TaskAutomationService.PreviewCount, preview.Count);
        Assert.All(preview, moment => Assert.True(moment > db.Clock.GetUtcNow()));

        var title = $"{count} {unit}";
        await service.CreateAsync(new CreateTaskAutomationRequest(
            title,
            IntervalUnit: unit,
            IntervalCount: count,
            StartsOn: new DateOnly(2026, 10, 20),
            TimeOfDay: new TimeOnly(6, 0)));

        // Far enough ahead that even three years has come round.
        db.Clock.Advance(TimeSpan.FromDays(4 * 366));

        Assert.Equal(new TaskAutomationRunSummary(1, 1, 0), await service.RunDueAsync());
        Assert.Equal(title, (await db.Context.Tasks.SingleAsync()).Title);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task An_interval_of_less_than_one_is_refused(int count)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(new CreateTaskAutomationRequest("Nonsense", IntervalCount: count)));

        var created = await service.CreateAsync(new CreateTaskAutomationRequest("Sensible", IntervalCount: 13));
        Assert.Equal(13, created.IntervalCount);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UpdateAsync(created.Id, new UpdateTaskAutomationRequest(IntervalCount: count)));
    }

    [Fact]
    public async Task A_thirteen_day_interval_keeps_its_own_rhythm_rather_than_a_weekly_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        var created = await service.CreateAsync(new CreateTaskAutomationRequest(
            "Every thirteen days",
            IntervalUnit: TaskIntervalUnit.Days,
            IntervalCount: 13,
            StartsOn: new DateOnly(2026, 10, 18),
            TimeOfDay: new TimeOnly(9, 0)));

        // The clock sits at 09:00 UTC on the 18th, which is 02:00 Pacific, so the 18th at 09:00
        // Pacific is still ahead and is the first run; the one after it is thirteen days on.
        Assert.Equal(new DateOnly(2026, 10, 18), Local(created.NextRunAt!.Value));

        var preview = service.Preview(TaskIntervalUnit.Days, 13, new DateOnly(2026, 10, 18), new TimeOnly(9, 0));
        Assert.Equal(
            [new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 13)],
            preview.Take(3).Select(Local));
    }

    private static TaskAutomationService Service(TestDatabase db)
    {
        db.Clock.SetLocalTimeZone(Pacific);
        return new TaskAutomationService(db.Context, db.Tasks(), db.Clock);
    }

    private static DateOnly Local(DateTimeOffset moment) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, Pacific).DateTime);
}
