using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CapybaraHome.Application.Tasks;
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
}
