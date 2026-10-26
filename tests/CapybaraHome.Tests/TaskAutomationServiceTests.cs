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
        Assert.Equal(TaskAutomationFrequency.Weekly, created.Frequency);
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
            TaskAutomationFrequency.Daily,
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
    }

    [Fact]
    public async Task A_run_that_has_not_come_due_does_nothing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        await service.CreateAsync(new CreateTaskAutomationRequest(
            "Not yet",
            Frequency: TaskAutomationFrequency.Monthly,
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
            Frequency: TaskAutomationFrequency.Daily,
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
            Frequency: TaskAutomationFrequency.Daily,
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
            Frequency: TaskAutomationFrequency.Daily,
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
            Frequency: TaskAutomationFrequency.Daily,
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
            Frequency: TaskAutomationFrequency.BiWeekly,
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
            Frequency: TaskAutomationFrequency.Quarterly,
            StartsOn: new DateOnly(2027, 1, 15),
            TimeOfDay: new TimeOnly(18, 30),
            ClearCategory: true,
            SkipWhenOpenTaskExists: true));

        Assert.Equal("Something else", updated!.Title);
        Assert.Equal("with notes", updated.Notes);
        Assert.Equal(TaskAutomationFrequency.Quarterly, updated.Frequency);
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
    [InlineData(TaskAutomationFrequency.Daily)]
    [InlineData(TaskAutomationFrequency.Weekly)]
    [InlineData(TaskAutomationFrequency.BiWeekly)]
    [InlineData(TaskAutomationFrequency.Monthly)]
    [InlineData(TaskAutomationFrequency.Quarterly)]
    [InlineData(TaskAutomationFrequency.Yearly)]
    public async Task Every_frequency_previews_and_runs(TaskAutomationFrequency frequency)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = Service(db);

        var preview = service.Preview(frequency, new DateOnly(2026, 10, 20), new TimeOnly(6, 0));
        Assert.Equal(TaskAutomationService.PreviewCount, preview.Count);
        Assert.All(preview, moment => Assert.True(moment > db.Clock.GetUtcNow()));

        await service.CreateAsync(new CreateTaskAutomationRequest(
            frequency.ToString(),
            Frequency: frequency,
            StartsOn: new DateOnly(2026, 10, 20),
            TimeOfDay: new TimeOnly(6, 0)));

        // Far enough ahead that the longest interval has come round.
        db.Clock.Advance(TimeSpan.FromDays(400));

        Assert.Equal(new TaskAutomationRunSummary(1, 1, 0), await service.RunDueAsync());
        Assert.Equal(frequency.ToString(), (await db.Context.Tasks.SingleAsync()).Title);
    }

    private static TaskAutomationService Service(TestDatabase db)
    {
        db.Clock.SetLocalTimeZone(Pacific);
        return new TaskAutomationService(db.Context, db.Tasks(), db.Clock);
    }

    private static DateOnly Local(DateTimeOffset moment) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, Pacific).DateTime);
}
