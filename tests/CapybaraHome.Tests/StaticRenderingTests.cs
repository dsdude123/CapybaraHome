using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CapybaraHome.Application.Jira;
using CapybaraHome.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CapybaraHome.Tests;

/// <summary>
/// Drives the pages the way a browser with no JavaScript does: plain form posts, no circuit. The
/// deployment this app runs on does not get an interactive circuit, so a page that needs one is
/// a page whose buttons do nothing, which is what these tests are here to stop.
/// </summary>
public class StaticRenderingTests
{
    private const string Myself = """{ "displayName": "Someone", "emailAddress": "someone@example.com" }""";

    private const string Projects = """
    { "isLast": true, "values": [ { "key": "AAA", "name": "A project" }, { "key": "BBB", "name": "Another" } ] }
    """;

    private const string Count = """{ "count": 2 }""";

    private const string Issues = """
    { "isLast": true, "issues": [
        { "key": "AAA-1", "fields": { "summary": "A bucket", "issuetype": { "name": "Epic" } } },
        { "key": "AAA-2", "fields": {
            "summary": "A job of work",
            "issuetype": { "name": "Task" },
            "status": { "name": "In Progress" },
            "parent": { "key": "AAA-1", "fields": { "issuetype": { "name": "Epic" } } } } } ] }
    """;

    [Theory]
    [InlineData("/")]
    [InlineData("/board")]
    [InlineData("/tasks")]
    [InlineData("/tasks/new")]
    [InlineData("/settings")]
    [InlineData("/settings/jira")]
    [InlineData("/account")]
    public async Task No_page_waits_for_an_interactive_circuit(string path)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var html = await client.GetStringAsync(path);

        // An interactive component leaves a marker comment in the HTML for the circuit to pick up.
        Assert.DoesNotContain("<!--Blazor:", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/board")]
    [InlineData("/tasks")]
    [InlineData("/tasks/new")]
    [InlineData("/settings")]
    [InlineData("/settings/jira")]
    [InlineData("/account")]
    [InlineData("/sign-in")]
    public async Task Every_form_carries_exactly_one_antiforgery_token(string path)
    {
        await using var app = await AppFactory.CreateAsync(stubSignIn: path != "/sign-in");
        var client = app.CreateClient();

        var html = await client.GetStringAsync(path);

        // EditForm writes its own token. A second one next to it posts both values, which the
        // browser sends as one header and the server rejects with a blank 400 page.
        foreach (var form in Forms(html).Where(f => f.Contains("method=\"post\"", StringComparison.OrdinalIgnoreCase)))
        {
            var tokens = System.Text.RegularExpressions.Regex.Matches(form, "__RequestVerificationToken").Count;
            Assert.True(tokens == 1, $"A form on {path} carries {tokens} antiforgery tokens.");
        }
    }

    [Fact]
    public async Task The_whole_jira_wizard_runs_on_form_posts()
    {
        var jira = new FakeHttp().Reply(Myself).Reply(Projects).Reply(Count).Reply(Issues);
        await using var app = await AppFactory.CreateAsync(configureServices: services =>
            services.AddHttpClient<JiraCloudClient>().ConfigurePrimaryHttpMessageHandler(jira.Handler));
        var client = Client(app);

        var connected = await PostAsync(client, "/settings/jira", "jira-connect", new()
        {
            ["connectForm.SiteUrl"] = "example.atlassian.net",
            ["connectForm.Email"] = "someone@example.com",
            ["connectForm.ApiToken"] = "a-token",
        });

        var step2 = await connected.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        Assert.Contains("2. Pick a project", step2);
        Assert.Contains("AAA", step2);

        // The token was used to connect and must not be written back into the page.
        Assert.DoesNotContain("a-token", step2);

        var ticket = Hidden(step2, "projectForm.Ticket");
        Assert.NotEmpty(ticket);

        var chosen = await PostAsync(client, "/settings/jira", "jira-project", new()
        {
            ["projectForm.Ticket"] = ticket,
            ["projectForm.ProjectKey"] = "AAA",
        });

        var step3 = await chosen.Content.ReadAsStringAsync();
        Assert.Contains("3. Confirm", step3);
        Assert.Contains("roughly 2 issue(s)", step3);

        var imported = await PostAsync(client, "/settings/jira", "jira-import", new()
        {
            ["importForm.Ticket"] = Hidden(step3, "importForm.Ticket"),
        });

        var step4 = await imported.Content.ReadAsStringAsync();
        Assert.Contains("4. Done", step4);
        Assert.Contains("1 task(s)", step4);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
        var task = await context.Tasks.Include(t => t.Category).SingleAsync();
        Assert.Equal("A job of work", task.Title);
        Assert.Equal("A bucket", task.Category!.Name);
    }

    [Fact]
    public async Task A_refused_token_is_shown_on_the_page_rather_than_swallowed()
    {
        var jira = new FakeHttp().Reply("""{ "message": "no" }""", HttpStatusCode.Unauthorized);
        await using var app = await AppFactory.CreateAsync(configureServices: services =>
            services.AddHttpClient<JiraCloudClient>().ConfigurePrimaryHttpMessageHandler(jira.Handler));
        var client = Client(app);

        var connected = await PostAsync(client, "/settings/jira", "jira-connect", new()
        {
            ["connectForm.SiteUrl"] = "example.atlassian.net",
            ["connectForm.Email"] = "someone@example.com",
            ["connectForm.ApiToken"] = "a-token",
        });

        var html = await connected.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, connected.StatusCode);
        Assert.Contains("API token was not accepted", html);
        Assert.Contains("1. Connect", html);
    }

    [Fact]
    public async Task A_ticket_the_store_has_dropped_sends_the_wizard_back_to_step_one()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await PostAsync(client, "/settings/jira", "jira-project", new()
        {
            ["projectForm.Ticket"] = "00000000000000000000000000000000",
            ["projectForm.ProjectKey"] = "AAA",
        });

        var html = await posted.Content.ReadAsStringAsync();
        Assert.Contains("the connection was dropped", html);
        Assert.Contains("1. Connect", html);
    }

    [Fact]
    public async Task An_export_file_uploads_and_loads()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var export = """
        {
          "formatVersion": 1,
          "exportedAt": "2026-10-22T00:00:00+00:00",
          "categories": [ { "id": 1, "name": "Restored", "colorHex": null, "sortOrder": 0 } ],
          "tasks": [ { "id": 1, "title": "A restored task", "notes": null, "categoryId": 1,
            "status": "ToDo", "dueDate": null, "completedAt": null, "jiraKey": null,
            "createdAt": "2026-10-01T00:00:00+00:00", "updatedAt": "2026-10-01T00:00:00+00:00" } ],
          "dashboardCards": []
        }
        """;

        var posted = await UploadAsync(client, export, "capybarahome-export.json");

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Contains("imported=", posted.Headers.Location!.OriginalString);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
        Assert.Equal("A restored task", (await context.Tasks.SingleAsync()).Title);
    }

    [Fact]
    public async Task A_file_that_is_not_an_export_comes_back_as_an_error_on_the_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await UploadAsync(client, "this is not JSON at all", "notes.txt");

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Contains("importError=", posted.Headers.Location!.OriginalString);

        var settings = await client.GetStringAsync(posted.Headers.Location!.OriginalString);
        Assert.Contains("not an export this build can read", settings);
    }

    [Fact]
    public async Task A_task_is_created_edited_and_moved_on_the_board_without_a_circuit()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var created = await PostAsync(client, "/tasks/new", "task-editor", new()
        {
            ["form.Title"] = "Mow the lawn",
            ["form.Notes"] = "The long grass by the shed",
            ["form.Status"] = nameof(Domain.Tasks.TaskItemStatus.ToDo),
            ["form.CategoryId"] = string.Empty,
        });

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var editor = created.Headers.Location!.OriginalString;
        Assert.Contains("tasks/", editor);

        var edited = await PostAsync(client, editor, "task-editor", new()
        {
            ["form.Title"] = "Mow the lawn and edge it",
            ["form.Notes"] = string.Empty,
            ["form.Status"] = nameof(Domain.Tasks.TaskItemStatus.InProgress),
            ["form.CategoryId"] = string.Empty,
        });

        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
        var task = await context.Tasks.SingleAsync();
        Assert.Equal("Mow the lawn and edge it", task.Title);
        Assert.Equal(Domain.Tasks.TaskItemStatus.InProgress, task.Status);

        var moved = await PostAsync(client, "/board", $"move-{task.Id}-{Domain.Tasks.TaskItemStatus.Done}", []);
        Assert.Equal(HttpStatusCode.Redirect, moved.StatusCode);

        context.ChangeTracker.Clear();
        Assert.Equal(Domain.Tasks.TaskItemStatus.Done, (await context.Tasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task The_task_filters_travel_in_the_query_string()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostAsync(client, "/tasks/new", "task-editor", new()
        {
            ["form.Title"] = "Fix the gate",
            ["form.Status"] = nameof(Domain.Tasks.TaskItemStatus.ToDo),
        });

        Assert.Contains("Fix the gate", await client.GetStringAsync("/tasks?text=gate"));
        Assert.DoesNotContain("Fix the gate", await client.GetStringAsync("/tasks?text=something-else"));
        Assert.DoesNotContain("Fix the gate", await client.GetStringAsync($"/tasks?status={nameof(Domain.Tasks.TaskItemStatus.Done)}"));
    }

    [Fact]
    public async Task A_category_is_added_and_deleted_from_the_settings_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var added = await PostAsync(client, "/settings", "add-category", new()
        {
            ["categoryForm.Name"] = "Garden",
        });

        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Contains("Garden", await client.GetStringAsync("/settings"));

        int id;
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            id = (await context.Categories.SingleAsync(c => c.Name == "Garden")).Id;
        }

        var deleted = await PostAsync(client, "/settings", $"delete-category-{id}", []);

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.DoesNotContain("Garden", await client.GetStringAsync("/settings"));
    }

    [Fact]
    public async Task A_connector_token_is_minted_and_shown_once()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var minted = await PostAsync(client, "/settings", "mint-token", new()
        {
            ["tokenForm.Name"] = "Phone",
        });

        var html = await minted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, minted.StatusCode);
        Assert.Contains("It is not shown again", html);
        Assert.DoesNotContain("It is not shown again", await client.GetStringAsync("/settings"));
    }

    private static IEnumerable<string> Forms(string html)
    {
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(html, "<form[\\s>].*?</form>",
            System.Text.RegularExpressions.RegexOptions.Singleline).ToList())
        {
            yield return match.Value;
        }
    }

    private static HttpClient Client(AppFactory app) =>
        app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string handler,
        Dictionary<string, string> fields)
    {
        fields["_handler"] = handler;
        fields["__RequestVerificationToken"] = await TokenAsync(client, path);

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string content, string fileName)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(await TokenAsync(client, "/settings")), "__RequestVerificationToken" },
            { new StringContent("Merge"), "mode" },
        };

        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        form.Add(file, "file", fileName);

        return await client.PostAsync("/settings/import", form);
    }

    // The page carries the token in a hidden field, which is also what sets the matching cookie.
    private static async Task<string> TokenAsync(HttpClient client, string path) =>
        Hidden(await client.GetStringAsync(path), "__RequestVerificationToken");

    // Blazor may put its own attributes between the name and the value, so the value is read
    // from the whole tag rather than from a fixed offset after the name.
    private static string Hidden(string html, string name)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html,
            $"name=\"{System.Text.RegularExpressions.Regex.Escape(name)}\"[^>]*?value=\"([^\"]*)\"");

        Assert.True(match.Success, $"No hidden field named {name} in the page.");
        return match.Groups[1].Value;
    }
}
