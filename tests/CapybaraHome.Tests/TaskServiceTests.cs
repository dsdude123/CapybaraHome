using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Tests;

/// <summary>Covers the filters and the status rules tasks are read and written through.</summary>
public class TaskServiceTests
{
    [Fact]
    public async Task Creating_a_task_trims_the_title_and_starts_it_to_do()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TaskService(db.Context, db.Clock);

        var created = await service.CreateAsync(new CreateTaskRequest("  Mow the lawn  "));

        Assert.Equal("Mow the lawn", created.Title);
        Assert.Equal(TaskItemStatus.ToDo, created.Status);
        Assert.Null(created.CompletedAt);
    }

    [Fact]
    public async Task A_blank_title_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TaskService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateTaskRequest("   ")));
    }

    [Fact]
    public async Task Moving_a_task_to_done_stamps_when_it_finished_and_moving_it_back_clears_that()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TaskService(db.Context, db.Clock);
        var created = await service.CreateAsync(new CreateTaskRequest("Fix the gate"));

        var done = await service.UpdateAsync(created.Id, new UpdateTaskRequest(Status: TaskItemStatus.Done));
        Assert.Equal(db.Clock.GetUtcNow(), done!.CompletedAt);

        var reopened = await service.UpdateAsync(created.Id, new UpdateTaskRequest(Status: TaskItemStatus.ToDo));
        Assert.Null(reopened!.CompletedAt);
    }

    [Fact]
    public async Task Filtering_by_days_away_keeps_only_the_tasks_due_inside_the_window()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TaskService(db.Context, db.Clock);
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().UtcDateTime);

        await service.CreateAsync(new CreateTaskRequest("Due tomorrow", DueDate: today.AddDays(1)));
        await service.CreateAsync(new CreateTaskRequest("Due next month", DueDate: today.AddDays(30)));
        await service.CreateAsync(new CreateTaskRequest("No due date"));

        var soon = await service.ListAsync(new TaskFilter(DueWithinDays: 7));

        Assert.Equal(["Due tomorrow"], soon.Select(t => t.Title));
    }

    [Fact]
    public async Task Overdue_leaves_out_what_is_already_done()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TaskService(db.Context, db.Clock);
        var today = DateOnly.FromDateTime(db.Clock.GetUtcNow().UtcDateTime);

        await service.CreateAsync(new CreateTaskRequest("Late", DueDate: today.AddDays(-2)));
        await service.CreateAsync(new CreateTaskRequest("Late but finished", Status: TaskItemStatus.Done, DueDate: today.AddDays(-2)));

        var overdue = await service.ListAsync(new TaskFilter(Overdue: true));

        Assert.Equal(["Late"], overdue.Select(t => t.Title));
    }

    [Fact]
    public async Task Searching_matches_the_title_and_the_notes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TaskService(db.Context, db.Clock);

        await service.CreateAsync(new CreateTaskRequest("Service the boiler"));
        await service.CreateAsync(new CreateTaskRequest("Call the plumber", "about the boiler"));
        await service.CreateAsync(new CreateTaskRequest("Buy milk"));

        var found = await service.ListAsync(new TaskFilter(Text: "boiler"));

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task A_category_that_does_not_exist_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new TaskService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CreateAsync(new CreateTaskRequest("Anything", CategoryId: 404)));
    }

    [Fact]
    public async Task Clearing_the_due_date_and_the_category_empties_both()
    {
        await using var db = await TestDatabase.CreateAsync();
        var categories = new CapybaraHome.Application.Categories.CategoryService(db.Context);
        var service = new TaskService(db.Context, db.Clock);

        var category = await categories.CreateAsync(new CapybaraHome.Application.Categories.CategoryRequest("Garden"));
        var created = await service.CreateAsync(new CreateTaskRequest(
            "Prune the hedge",
            CategoryId: category.Id,
            DueDate: new DateOnly(2026, 11, 1)));

        var cleared = await service.UpdateAsync(created.Id, new UpdateTaskRequest(ClearCategory: true, ClearDueDate: true));

        Assert.Null(cleared!.CategoryId);
        Assert.Null(cleared.DueDate);
    }
}
