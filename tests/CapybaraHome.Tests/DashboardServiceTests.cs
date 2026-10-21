using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Dashboards;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Tests;

/// <summary>Covers the numbers the dashboard cards report and the renaming the UI relies on.</summary>
public class DashboardServiceTests
{
    [Fact]
    public async Task A_progress_card_reports_the_share_of_its_category_that_is_done()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (tasks, categories, dashboard) = Services(db);
        var category = await categories.CreateAsync(new CategoryRequest("Loft conversion"));

        await tasks.CreateAsync(new CreateTaskRequest("One", CategoryId: category.Id, Status: TaskItemStatus.Done));
        await tasks.CreateAsync(new CreateTaskRequest("Two", CategoryId: category.Id, Status: TaskItemStatus.Done));
        await tasks.CreateAsync(new CreateTaskRequest("Three", CategoryId: category.Id));
        await tasks.CreateAsync(new CreateTaskRequest("Unrelated"));

        var card = await dashboard.CreateAsync(new DashboardCardRequest(
            "Loft conversion",
            DashboardCardKind.CategoryProgress,
            CategoryId: category.Id));

        Assert.Equal(3, card.Total);
        Assert.Equal(2, card.Done);
        Assert.Equal(2d / 3, card.Progress, 5);
    }

    [Fact]
    public async Task A_progress_card_without_a_category_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, _, dashboard) = Services(db);

        await Assert.ThrowsAsync<ArgumentException>(() => dashboard.CreateAsync(
            new DashboardCardRequest("Nowhere", DashboardCardKind.CategoryProgress)));
    }

    [Fact]
    public async Task A_count_card_counts_only_what_its_filter_matches()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (tasks, _, dashboard) = Services(db);

        await tasks.CreateAsync(new CreateTaskRequest("Started", Status: TaskItemStatus.InProgress));
        await tasks.CreateAsync(new CreateTaskRequest("Waiting"));

        var card = await dashboard.CreateAsync(new DashboardCardRequest(
            "On the go",
            DashboardCardKind.TaskCount,
            Status: TaskItemStatus.InProgress));

        Assert.Equal(1, card.Total);
    }

    [Fact]
    public async Task Cards_can_be_renamed_without_touching_their_filter()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, _, dashboard) = Services(db);
        var card = await dashboard.CreateAsync(new DashboardCardRequest(
            "Due soon",
            DashboardCardKind.TaskList,
            DueWithinDays: 7));

        var renamed = await dashboard.RenameAsync(card.Id, "This week");

        Assert.Equal("This week", renamed!.Title);
        Assert.Equal(7, renamed.DueWithinDays);
    }

    [Fact]
    public async Task Reordering_puts_the_cards_in_the_order_given()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, _, dashboard) = Services(db);
        var first = await dashboard.CreateAsync(new DashboardCardRequest("First", DashboardCardKind.TaskCount));
        var second = await dashboard.CreateAsync(new DashboardCardRequest("Second", DashboardCardKind.TaskCount));

        await dashboard.ReorderAsync([second.Id, first.Id]);

        var cards = await dashboard.GetDashboardAsync();
        Assert.Equal(["Second", "First"], cards.Select(c => c.Title));
    }

    [Fact]
    public async Task Seeding_a_fresh_installation_adds_cards_once()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (_, _, dashboard) = Services(db);

        await dashboard.SeedDefaultsAsync();
        var afterFirst = await dashboard.GetDashboardAsync();

        await dashboard.SeedDefaultsAsync();
        var afterSecond = await dashboard.GetDashboardAsync();

        Assert.NotEmpty(afterFirst);
        Assert.Equal(afterFirst.Count, afterSecond.Count);
    }

    private static (TaskService Tasks, CategoryService Categories, DashboardService Dashboard) Services(TestDatabase db)
    {
        var tasks = new TaskService(db.Context, db.Clock);
        return (tasks, new CategoryService(db.Context), new DashboardService(db.Context, tasks));
    }
}
