using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Web;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Tests;

/// <summary>Runs the real host and checks that the pages render and the API answers.</summary>
public class WebAppTests
{
    // The API writes statuses as names, so a client reading them back needs the same converter.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Theory]
    [InlineData("/")]
    [InlineData("/board")]
    [InlineData("/tasks")]
    [InlineData("/tasks/new")]
    [InlineData("/settings")]
    [InlineData("/account")]
    [InlineData("/Error")]
    public async Task Every_page_renders(string path)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("CapybaraHome", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_answers_without_signing_in()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_task_created_through_the_api_comes_back_from_it()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var created = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Bleed the radiators"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var task = await created.Content.ReadFromJsonAsync<TaskView>(Json);
        Assert.Equal("Bleed the radiators", task!.Title);

        var listed = await client.GetFromJsonAsync<List<TaskView>>("/api/tasks", Json);
        Assert.Contains(listed!, t => t.Id == task.Id);
    }

    [Fact]
    public async Task Statuses_travel_as_names()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Sweep the chimney", Status: TaskItemStatus.OnHold));
        var json = await client.GetStringAsync("/api/tasks");

        Assert.Contains("\"status\":\"OnHold\"", json);
    }

    [Fact]
    public async Task A_task_with_no_title_is_refused()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var response = await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("  "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_export_round_trips_through_the_api()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Wash the windows"));

        var export = await client.GetStringAsync("/api/export");
        Assert.Contains("Wash the windows", export);

        var import = await client.PostAsync(
            "/api/import?mode=Merge",
            new StringContent(export, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
    }

    [Fact]
    public async Task A_due_date_is_shown_with_its_month_name_and_year()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Service the boiler", DueDate: new DateOnly(2026, 10, 23)));

        var tasks = await client.GetStringAsync("/tasks");
        var board = await client.GetStringAsync("/board");

        Assert.Contains("Due October 23, 2026", tasks);
        Assert.Contains("Due October 23, 2026", board);
    }

    [Theory]
    [InlineData("/tasks")]
    [InlineData("/board")]
    public async Task Every_sort_the_picker_offers_renders(string path)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        var category = await client.PostAsJsonAsync("/api/categories", new CategoryRequest("Indoors"));
        var filed = await category.Content.ReadFromJsonAsync<CategoryView>(Json);

        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Dated and filed", CategoryId: filed!.Id, DueDate: new DateOnly(2026, 11, 1)));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Undated and loose"));

        foreach (var sort in TaskSortNames.All)
        {
            var response = await client.GetAsync($"{path}?sort={TaskSortNames.Value(sort)}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Undated and loose", await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("/tasks")]
    [InlineData("/board")]
    public async Task A_sorted_page_follows_the_order_the_query_string_asks_for(string path)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Alpha", DueDate: new DateOnly(2026, 12, 1)));
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Omega", DueDate: new DateOnly(2026, 11, 1)));

        var byDueDate = await client.GetStringAsync(path);
        var byTitle = await client.GetStringAsync(path + "?sort=title");

        Assert.True(byDueDate.IndexOf("Omega", StringComparison.Ordinal) < byDueDate.IndexOf("Alpha", StringComparison.Ordinal));
        Assert.True(byTitle.IndexOf("Alpha", StringComparison.Ordinal) < byTitle.IndexOf("Omega", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_fresh_installation_comes_up_with_a_dashboard()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var cards = await client.GetFromJsonAsync<List<Dictionary<string, object>>>("/api/dashboard/cards");

        Assert.NotEmpty(cards!);
    }

    [Fact]
    public async Task The_task_list_shows_one_page_at_a_time_and_links_to_the_next()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        for (var i = 0; i < 55; i++)
        {
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"Task {i:D2}", DueDate: new DateOnly(2026, 11, 1).AddDays(i)));
        }

        var first = await client.GetStringAsync("/tasks");

        Assert.Contains("Showing 1-50 of 55 tasks", first);
        Assert.Contains("Task 49", first);
        Assert.DoesNotContain("Task 50", first);
        Assert.Contains("page=2", first);

        var second = await client.GetStringAsync("/tasks?page=2");

        Assert.Contains("Showing 51-55 of 55 tasks", second);
        Assert.Contains("Task 54", second);
        Assert.DoesNotContain("Task 49", second);
    }

    [Fact]
    public async Task A_page_past_the_last_one_falls_back_to_the_last_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Only one"));

        var page = await client.GetStringAsync("/tasks?page=99");

        Assert.Contains("Only one", page);
        Assert.Contains("Showing 1-1 of 1 task", page);
    }

    [Fact]
    public async Task A_page_link_keeps_the_filters_and_the_order()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        for (var i = 0; i < 51; i++)
        {
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"Task {i:D2}", DueDate: new DateOnly(2020, 1, 1).AddDays(i)));
        }

        var page = await client.GetStringAsync("/tasks?sort=title&overdue=true&text=Task");

        Assert.Contains("sort=title", page);
        Assert.Contains("overdue=true", page);
        Assert.Contains("text=Task", page);
    }

    [Theory]
    [InlineData(TaskItemStatus.ToDo, "todo")]
    [InlineData(TaskItemStatus.InProgress, "progress")]
    [InlineData(TaskItemStatus.OnHold, "hold")]
    [InlineData(TaskItemStatus.Done, "done")]
    public async Task A_board_column_shows_one_page_of_cards_and_loads_the_rest(TaskItemStatus status, string parameter)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        for (var i = 0; i < 25; i++)
        {
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"Task {i:D2}", Status: status, DueDate: new DateOnly(2026, 11, 1).AddDays(i)));
        }

        var board = await client.GetStringAsync("/board");

        Assert.Contains("Task 19", board);
        Assert.DoesNotContain("Task 20", board);
        Assert.Contains("Load 5 more", board);
        Assert.Contains($"{parameter}=40", board);

        var expanded = await client.GetStringAsync($"/board?{parameter}=40");

        Assert.Contains("Task 24", expanded);
        Assert.DoesNotContain("Load", expanded[expanded.IndexOf("Task 24", StringComparison.Ordinal)..]);
    }

    [Fact]
    public async Task A_column_size_typed_past_the_end_shows_the_column_whole_and_no_more()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        for (var i = 0; i < 25; i++)
        {
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"Task {i:D2}", DueDate: new DateOnly(2026, 11, 1).AddDays(i)));
        }

        var board = await client.GetStringAsync("/board?todo=100000000");

        Assert.Contains("Task 24", board);
        Assert.DoesNotContain("Load", board);
    }

    [Fact]
    public async Task An_empty_task_list_says_so_and_offers_no_pages()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Only one"));

        var page = await client.GetStringAsync("/tasks?text=nothing-matches-this");

        Assert.Contains("No tasks", page);
        Assert.DoesNotContain("pager", page);
    }

    [Fact]
    public async Task A_board_column_that_fits_offers_no_load_more()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Only one"));

        var board = await client.GetStringAsync("/board");

        Assert.DoesNotContain("more", board);
    }

    [Fact]
    public async Task A_board_load_more_link_keeps_the_other_columns_and_the_order()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        for (var i = 0; i < 25; i++)
        {
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"To do {i:D2}"));
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"Done {i:D2}", Status: TaskItemStatus.Done));
        }

        var board = await client.GetStringAsync("/board?done=40&sort=title");

        // A column's size is reported as the column holds, so Done asks for the 25 it has, not the 40 typed.
        Assert.Contains("todo=40&amp;done=25", board);
        Assert.Contains("sort=title", board);
    }

    [Fact]
    public async Task Every_combination_of_status_checkboxes_lists_exactly_those_statuses()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var titles = new Dictionary<TaskItemStatus, string>();

        foreach (var status in StatusNames.All)
        {
            var title = $"Work in {status}";
            titles[status] = title;
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest(title, Status: status));
        }

        for (var mask = 0; mask < 1 << 4; mask++)
        {
            var chosen = StatusNames.All.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            var query = string.Join("&", chosen.Select(status => $"status={status}"));
            var response = await client.GetAsync("/tasks?" + query);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var html = await response.Content.ReadAsStringAsync();

            foreach (var status in StatusNames.All)
            {
                // Nothing checked is the unfiltered list, so every task shows.
                var shown = chosen.Count == 0 || chosen.Contains(status);

                Assert.Equal(shown, html.Contains(titles[status], StringComparison.Ordinal));
                Assert.Equal(chosen.Contains(status), Checked(html, $"filter-status-{status}"));
            }
        }
    }

    [Fact]
    public async Task A_page_link_keeps_every_checked_status()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        for (var i = 0; i < 30; i++)
        {
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"To do {i:D2}"));
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"Going {i:D2}", Status: TaskItemStatus.InProgress));
            await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest($"Held {i:D2}", Status: TaskItemStatus.OnHold));
        }

        var first = await client.GetStringAsync("/tasks?status=ToDo&status=InProgress&sort=title");

        Assert.Contains("Showing 1-50 of 60 tasks", first);
        Assert.Contains("status=ToDo", first);
        Assert.Contains("status=InProgress", first);
        Assert.Contains("sort=title", first);
        Assert.DoesNotContain("Held 00", first);

        var second = await client.GetStringAsync("/tasks?status=ToDo&status=InProgress&sort=title&page=2");

        Assert.Contains("Showing 51-60 of 60 tasks", second);
        Assert.DoesNotContain("Held 00", second);
    }

    [Fact]
    public async Task A_status_the_filter_does_not_offer_is_ignored()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();
        await client.PostAsJsonAsync("/api/tasks", new CreateTaskRequest("Only one"));

        var html = await client.GetStringAsync("/tasks?status=Nonsense");

        Assert.Contains("Only one", html);
        Assert.DoesNotContain("status=Nonsense", html);
    }

    /// <summary>Returns whether the checkbox with <paramref name="id"/> is rendered checked.</summary>
    private static bool Checked(string html, string id)
    {
        var tag = System.Text.RegularExpressions.Regex.Match(html, $"<input[^>]*id=\"{id}\"[^>]*>");

        Assert.True(tag.Success, $"No input with id {id}");

        return tag.Value.Contains("checked", StringComparison.Ordinal);
    }
}
