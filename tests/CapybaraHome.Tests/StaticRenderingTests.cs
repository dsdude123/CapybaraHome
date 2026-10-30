using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CapybaraHome.Application.Grocery;
using CapybaraHome.Application.Jira;
using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Tasks;
using CapybaraHome.Infrastructure.Persistence;
using CapybaraHome.Web;
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
    [InlineData("/automations")]
    [InlineData("/automations/new")]
    [InlineData("/meals")]
    [InlineData("/meals/new")]
    [InlineData("/meals/plan")]
    [InlineData("/report")]
    [InlineData("/report/settings")]
    [InlineData("/grocery")]
    [InlineData("/grocery/generate")]
    [InlineData("/grocery/print")]
    [InlineData("/meals/1/print")]
    [InlineData("/meals/1/variants/1/print")]
    [InlineData("/meals/tags")]
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
    [InlineData("/automations")]
    [InlineData("/automations/new")]
    [InlineData("/meals")]
    [InlineData("/meals/new")]
    [InlineData("/meals/plan")]
    [InlineData("/report")]
    [InlineData("/report/settings")]
    [InlineData("/grocery")]
    [InlineData("/grocery/generate")]
    [InlineData("/grocery/print")]
    [InlineData("/meals/1/print")]
    [InlineData("/meals/1/variants/1/print")]
    [InlineData("/meals/tags")]
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

    [Theory]
    [InlineData("/tasks", "tasks/new")]
    [InlineData("/board", "tasks/new")]
    [InlineData("/automations", "automations/new")]
    [InlineData("/meals", "meals/new")]
    [InlineData("/meals/plan", "meals/new")]
    public async Task Every_list_page_offers_its_own_create_link(string path, string href)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var head = PageHead(await client.GetStringAsync(path));

        Assert.Contains($"href=\"{href}\"", head);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/board")]
    [InlineData("/tasks")]
    [InlineData("/tasks/new")]
    [InlineData("/automations")]
    [InlineData("/automations/new")]
    [InlineData("/meals")]
    [InlineData("/meals/new")]
    [InlineData("/meals/plan")]
    [InlineData("/settings")]
    [InlineData("/settings/jira")]
    [InlineData("/account")]
    public async Task The_nav_bar_offers_no_create_link_of_its_own(string path)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = app.CreateClient();

        var nav = Nav(await client.GetStringAsync(path));

        // One create button in the header meant the Meals page showed a Create for tasks next to
        // its own; creating now belongs to the page being looked at.
        Assert.DoesNotContain("/new\"", nav);
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
    public async Task The_wizard_imports_a_board_as_meals_after_sorting_its_labels()
    {
        const string Food = """
        { "isLast": true, "issues": [
            { "key": "FOOD-1", "fields": {
                "summary": "A meal",
                "status": { "name": "Queued" },
                "labels": [ "quick" ],
                "updated": "2026-06-01T00:00:00.000+0000" } } ] }
        """;

        var jira = new FakeHttp().Reply(Myself).Reply(Projects).Reply(Count).Reply(Food);
        await using var app = await AppFactory.CreateAsync(configureServices: services =>
            services.AddHttpClient<JiraCloudClient>().ConfigurePrimaryHttpMessageHandler(jira.Handler));
        var client = Client(app);

        var connected = await PostAsync(client, "/settings/jira", "jira-connect", new()
        {
            ["connectForm.SiteUrl"] = "example.atlassian.net",
            ["connectForm.Email"] = "someone@example.com",
            ["connectForm.ApiToken"] = "a-token",
        });

        var ticket = Hidden(await connected.Content.ReadAsStringAsync(), "projectForm.Ticket");

        var chosen = await PostAsync(client, "/settings/jira", "jira-project", new()
        {
            ["projectForm.Ticket"] = ticket,
            ["projectForm.ProjectKey"] = "AAA",
        });

        var confirm = await chosen.Content.ReadAsStringAsync();
        Assert.Contains("Import these issues as", confirm);

        var asMeals = await PostAsync(client, "/settings/jira", "jira-import", new()
        {
            ["importForm.Ticket"] = Hidden(confirm, "importForm.Ticket"),
            ["importForm.Kind"] = "meals",
        });

        var facets = await asMeals.Content.ReadAsStringAsync();
        Assert.Contains("Sort the labels", facets);
        Assert.Contains("quick", facets);

        var imported = await PostManyAsync(client, "/settings/jira", "jira-facets",
        [
            new("ticket", Hidden(facets, "ticket")),
            new("label", "quick"),
            new("facet", nameof(MealTagFacet.Effort)),
        ]);

        var done = await imported.Content.ReadAsStringAsync();
        Assert.Contains("Added 1 meal(s)", done);

        using var scope = app.Services.CreateScope();
        var meals = scope.ServiceProvider.GetRequiredService<MealService>();
        var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
        var meal = Assert.Single(await meals.ListAsync(new MealFilter()));

        Assert.Equal("A meal", meal.Name);
        Assert.Equal(MealStatus.Queued, meal.Status);
        Assert.Equal(new DateOnly(2026, 6, 1), meal.LastCookedAt);
        Assert.Equal(MealTagFacet.Effort, Assert.Single(await tags.ListAsync()).Facet);

        // The labels were sorted against the same board that was imported, read once.
        Assert.Single(jira.Requests, r => r.RequestUri!.AbsolutePath.Contains("search/jql"));
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

    [Fact]
    public async Task A_meal_is_created_with_its_ingredients_and_edited_without_a_circuit()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var created = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", "save"),
            new("name", "A meal"),
            new("status", nameof(MealStatus.Queued)),
            new("last-cooked", string.Empty),
            new("directions", "Cook it"),
            new("quantity", "2 cups"),
            new("item", "first item"),
            new("quantity", string.Empty),
            new("item", "second item"),
            new("new-tags", "quick, oven"),
            new("new-tag-facet", nameof(MealTagFacet.Effort)),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var editor = created.Headers.Location!.OriginalString;

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            var meal = await context.Meals.Include(m => m.Ingredients).Include(m => m.Tags).SingleAsync();

            Assert.Equal("A meal", meal.Name);
            Assert.Equal(MealStatus.Queued, meal.Status);
            Assert.Equal(["first item", "second item"], meal.Ingredients.OrderBy(i => i.SortOrder).Select(i => i.Item));
            Assert.Equal("2 cups", meal.Ingredients.OrderBy(i => i.SortOrder).First().Quantity);
            Assert.Equal(["oven", "quick"], meal.Tags.Select(t => t.Name).Order());
        }

        var edited = await PostManyAsync(client, editor, "meal-editor",
        [
            new("action", "save"),
            new("name", "A renamed meal"),
            new("status", nameof(MealStatus.Available)),
            new("last-cooked", string.Empty),
            new("directions", string.Empty),
            new("item", "first item"),
            new("quantity", "2 cups"),
            new("new-tags", string.Empty),
            new("new-tag-facet", nameof(MealTagFacet.Effort)),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            var meal = await context.Meals.Include(m => m.Ingredients).SingleAsync();

            Assert.Equal("A renamed meal", meal.Name);
            Assert.Equal(MealStatus.Available, meal.Status);
            Assert.NotNull(meal.LastCookedAt);
            Assert.Single(meal.Ingredients);
        }
    }

    [Fact]
    public async Task Asking_for_more_ingredient_lines_keeps_what_was_typed()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", "add-row"),
            new("name", "Half typed"),
            new("status", nameof(MealStatus.Pooled)),
            new("directions", "so far"),
            new("quantity", "1 tin"),
            new("item", "an item"),
            new("new-tags", "a new tag"),
            new("new-tag-facet", nameof(MealTagFacet.Trait)),
        ]);

        var html = await posted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Contains("Half typed", html);
        Assert.Contains("1 tin", html);
        Assert.Contains("an item", html);

        // The tags typed into the box are only created on a save, so they have to come back with
        // the page or a press of Add lines throws them away.
        Assert.Contains("a new tag", html);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
        Assert.Empty(context.Meals);
    }

    [Fact]
    public async Task A_meal_saved_without_a_name_says_so_on_the_editor()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var refused = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", "save"),
            new("name", "   "),
            new("status", nameof(MealStatus.Pooled)),
            new("last-cooked", string.Empty),
            new("directions", string.Empty),
            new("quantity", "1 tin"),
            new("item", "an item"),
            new("new-tags", string.Empty),
            new("new-tag-facet", nameof(MealTagFacet.Trait)),
        ]);

        var html = await refused.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("class=\"error\"", html);
        Assert.Contains("an item", html);

        using var scope = app.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>().Meals);
    }

    [Fact]
    public async Task A_variant_editor_handles_a_meal_with_no_ingredients_a_blank_name_and_a_meal_that_is_gone()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        Assert.Contains("That meal is gone.", await client.GetStringAsync("/meals/404/variants/new"));

        int mealId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            mealId = (await meals.CreateAsync(new CreateMealRequest("A meal"))).Id;
        }

        var blank = await client.GetStringAsync($"/meals/{mealId}/variants/new");
        Assert.Contains("The meal has no ingredients to leave out.", blank);

        var refused = await PostManyAsync(client, $"/meals/{mealId}/variants/new", "variant-editor",
        [
            new("action", "save"),
            new("name", "  "),
            new("directions", string.Empty),
            new("quantity", "1 tin"),
            new("item", "an extra"),
        ]);

        var html = await refused.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("class=\"error\"", html);
        Assert.Contains("an extra", html);

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            Assert.Empty((await meals.GetAsync(mealId))!.Variants);
        }
    }

    [Fact]
    public async Task A_fresh_meal_editor_offers_one_ingredient_line_and_an_edited_one_only_its_own()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        Assert.Equal(1, Inputs(await client.GetStringAsync("/meals/new"), "item"));

        int mealId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = await meals.CreateAsync(new CreateMealRequest("A meal", Ingredients:
                [new MealIngredientInput("first"), new MealIngredientInput("second")]));
            mealId = meal.Id;
        }

        var editor = await client.GetStringAsync($"/meals/{mealId}");
        Assert.Equal(2, Inputs(editor, "item"));
        Assert.Equal(2, Inputs(editor, "quantity"));
    }

    [Fact]
    public async Task Adding_and_removing_an_ingredient_line_keeps_the_rest_of_the_list()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var added = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", "add-row"),
            new("name", "A meal"),
            new("status", nameof(MealStatus.Pooled)),
            new("directions", string.Empty),
            new("quantity", "1 tin"),
            new("item", "an item"),
            new("new-tags", string.Empty),
            new("new-tag-facet", nameof(MealTagFacet.Trait)),
        ]);

        var withTwo = await added.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(2, Inputs(withTwo, "item"));

        var removed = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", IngredientRow.RemoveAction(0)),
            new("name", "A meal"),
            new("status", nameof(MealStatus.Pooled)),
            new("directions", string.Empty),
            new("quantity", "1 tin"),
            new("item", "the first"),
            new("quantity", "2 tins"),
            new("item", "the second"),
            new("new-tags", string.Empty),
            new("new-tag-facet", nameof(MealTagFacet.Trait)),
        ]);

        var withOne = await removed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(1, Inputs(withOne, "item"));
        Assert.Contains("the second", withOne);
        Assert.DoesNotContain("the first", withOne);

        // The line was dropped in the editor only; nothing was written.
        using var scope = app.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>().Meals);
    }

    [Fact]
    public async Task Tags_of_several_facets_are_added_without_leaving_the_meal_editor()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var first = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", "add-tags"),
            new("name", "A meal"),
            new("status", nameof(MealStatus.Pooled)),
            new("directions", "Cook it"),
            new("quantity", "1 tin"),
            new("item", "an item"),
            new("new-tags", "an effort tag"),
            new("new-tag-facet", nameof(MealTagFacet.Effort)),
        ]);

        var afterEffort = await first.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("Added 1 tag(s).", afterEffort);
        Assert.Contains("Effort", afterEffort);

        // What was typed elsewhere survives, and the box is emptied for the next facet.
        Assert.Contains("A meal", afterEffort);
        Assert.Contains("an item", afterEffort);
        Assert.DoesNotContain("value=\"an effort tag\"", afterEffort);

        int effortId;
        using (var scope = app.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            effortId = Assert.Single(await tags.ListAsync()).Id;
        }

        var second = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", "add-tags"),
            new("name", "A meal"),
            new("status", nameof(MealStatus.Pooled)),
            new("directions", "Cook it"),
            new("quantity", "1 tin"),
            new("item", "an item"),
            new("tag", effortId.ToString()),
            new("new-tags", "a cuisine tag"),
            new("new-tag-facet", nameof(MealTagFacet.Cuisine)),
        ]);

        var afterCuisine = await second.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("Added 1 tag(s).", afterCuisine);
        Assert.Contains("Cuisine", afterCuisine);

        int cuisineId;
        using (var scope = app.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            cuisineId = (await tags.ListAsync()).Single(t => t.Facet == MealTagFacet.Cuisine).Id;
        }

        var saved = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", "save"),
            new("name", "A meal"),
            new("status", nameof(MealStatus.Pooled)),
            new("last-cooked", string.Empty),
            new("directions", "Cook it"),
            new("quantity", "1 tin"),
            new("item", "an item"),
            new("tag", effortId.ToString()),
            new("tag", cuisineId.ToString()),
            new("new-tags", string.Empty),
            new("new-tag-facet", nameof(MealTagFacet.Trait)),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = Assert.Single(await meals.ListAsync(new MealFilter()));
            Assert.Equal(
                [MealTagFacet.Cuisine, MealTagFacet.Effort],
                (await meals.GetAsync(meal.Id))!.Tags.Select(t => t.Facet).Order());
        }
    }

    [Fact]
    public async Task A_blank_tag_is_refused_by_the_meal_editor_without_losing_what_was_typed()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await PostManyAsync(client, "/meals/new", "meal-editor",
        [
            new("action", "add-tags"),
            new("name", "A meal"),
            new("status", nameof(MealStatus.Pooled)),
            new("directions", string.Empty),
            new("quantity", string.Empty),
            new("item", "an item"),
            new("new-tags", ","),
            new("new-tag-facet", nameof(MealTagFacet.Trait)),
        ]);

        var html = await posted.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Contains("Added 0 tag(s).", html);
        Assert.Contains("an item", html);
    }

    [Theory]
    [InlineData(nameof(MealTagFacet.Cuisine), "Cuisine")]
    [InlineData(nameof(MealTagFacet.Protein), "Protein")]
    [InlineData(nameof(MealTagFacet.Effort), "Effort")]
    [InlineData(nameof(MealTagFacet.Appliance), "Appliance")]
    [InlineData(nameof(MealTagFacet.Trait), "Trait")]
    public async Task A_tag_is_created_in_every_facet_the_page_offers(string facet, string heading)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var created = await PostAsync(client, "/meals/tags", "tag-create", new()
        {
            ["name"] = "a tag",
            ["facet"] = facet,
        });

        var html = await created.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains(heading, html);
        Assert.Contains("0 meal(s)", html);

        using var scope = app.Services.CreateScope();
        var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
        var tag = Assert.Single(await tags.ListUsageAsync());
        Assert.Equal(facet, tag.Facet.ToString());
    }

    [Fact]
    public async Task The_tag_page_refuses_a_blank_name_and_counts_the_meals_carrying_each_tag()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var refused = await PostAsync(client, "/meals/tags", "tag-create", new()
        {
            ["name"] = "   ",
            ["facet"] = nameof(MealTagFacet.Trait),
        });

        var error = await refused.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("A tag needs a name.", error);
        Assert.Contains("No tags yet.", error);

        using (var scope = app.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var tag = await tags.EnsureAsync(new MealTagRequest("a tag", MealTagFacet.Trait));
            await meals.CreateAsync(new CreateMealRequest("A meal", TagIds: [tag.Id]));
        }

        Assert.Contains("1 meal(s)", await client.GetStringAsync("/meals/tags"));
    }

    [Fact]
    public async Task A_tag_is_renamed_and_moved_to_another_facet_without_a_circuit()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int tagId;
        using (var scope = app.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            tagId = (await tags.EnsureAsync(new MealTagRequest("mistyped", MealTagFacet.Trait))).Id;
            await tags.EnsureAsync(new MealTagRequest("taken", MealTagFacet.Trait));
        }

        var saved = await PostAsync(client, $"/meals/tags/{tagId}", "tag-editor", new()
        {
            ["action"] = "save",
            ["name"] = "corrected",
            ["facet"] = nameof(MealTagFacet.Appliance),
        });

        var html = await saved.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Contains("Saved.", html);

        using (var scope = app.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            var tag = await tags.GetAsync(tagId);
            Assert.Equal("corrected", tag!.Name);
            Assert.Equal(MealTagFacet.Appliance, tag.Facet);
        }

        // A name another tag already carries is refused rather than breaking the unique index.
        var clash = await PostAsync(client, $"/meals/tags/{tagId}", "tag-editor", new()
        {
            ["action"] = "save",
            ["name"] = "TAKEN",
            ["facet"] = nameof(MealTagFacet.Appliance),
        });

        var refused = await clash.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, clash.StatusCode);
        Assert.Contains("Another tag is already called", refused);

        var blank = await PostAsync(client, $"/meals/tags/{tagId}", "tag-editor", new()
        {
            ["action"] = "save",
            ["name"] = "  ",
            ["facet"] = nameof(MealTagFacet.Appliance),
        });

        Assert.Contains("A tag needs a name.", await blank.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Deleting_a_tag_names_the_meals_it_will_change_before_it_goes()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int tagId;
        int mealId;
        using (var scope = app.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            tagId = (await tags.EnsureAsync(new MealTagRequest("a tag", MealTagFacet.Trait))).Id;
            mealId = (await meals.CreateAsync(new CreateMealRequest("A meal", TagIds: [tagId]))).Id;
        }

        var asked = await PostAsync(client, $"/meals/tags/{tagId}", "tag-editor", new()
        {
            ["action"] = "delete",
            ["name"] = "a tag",
            ["facet"] = nameof(MealTagFacet.Trait),
        });

        var warning = await asked.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Contains("takes it off 1 meal(s)", warning);
        Assert.Contains("A meal", warning);
        Assert.Contains("delete-confirmed", warning);

        // Asking and then thinking better of it leaves the tag alone.
        var kept = await PostAsync(client, $"/meals/tags/{tagId}", "tag-editor", new()
        {
            ["action"] = "cancel-delete",
            ["name"] = "a tag",
            ["facet"] = nameof(MealTagFacet.Trait),
        });

        Assert.DoesNotContain("delete-confirmed", await kept.Content.ReadAsStringAsync());

        var gone = await PostAsync(client, $"/meals/tags/{tagId}", "tag-editor", new()
        {
            ["action"] = "delete-confirmed",
            ["name"] = "a tag",
            ["facet"] = nameof(MealTagFacet.Trait),
        });

        Assert.Equal(HttpStatusCode.Redirect, gone.StatusCode);
        Assert.EndsWith("/meals/tags", gone.Headers.Location!.OriginalString);

        using (var scope = app.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            Assert.Empty(await tags.ListUsageAsync());
            Assert.Empty((await meals.GetAsync(mealId))!.Tags);
        }
    }

    [Fact]
    public async Task A_tag_that_no_meal_carries_says_so_before_it_is_deleted()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int tagId;
        using (var scope = app.Services.CreateScope())
        {
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            tagId = (await tags.EnsureAsync(new MealTagRequest("a tag", MealTagFacet.Trait))).Id;
        }

        var asked = await PostAsync(client, $"/meals/tags/{tagId}", "tag-editor", new()
        {
            ["action"] = "delete",
            ["name"] = "a tag",
            ["facet"] = nameof(MealTagFacet.Trait),
        });

        Assert.Contains("No meal carries it.", await asked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_tag_that_is_already_gone_shows_a_page_rather_than_an_error()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var html = await client.GetStringAsync("/meals/tags/404");

        Assert.Contains("That tag is gone.", html);
        Assert.DoesNotContain("<!--Blazor:", html);
    }

    [Fact]
    public async Task A_variant_is_added_and_edited_from_the_meal_page_without_a_circuit()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int mealId;
        int ingredientId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = await meals.CreateAsync(new CreateMealRequest("A meal", Ingredients:
                [new MealIngredientInput("first"), new MealIngredientInput("second")]));
            mealId = meal.Id;
            ingredientId = meal.Ingredients[1].Id;
        }

        var blank = await client.GetStringAsync($"/meals/{mealId}/variants/new");
        Assert.DoesNotContain("<!--Blazor:", blank);
        foreach (var form in Forms(blank).Where(f => f.Contains("method=\"post\"", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(form, "__RequestVerificationToken"));
        }

        var added = await PostManyAsync(client, $"/meals/{mealId}/variants/new", "variant-editor",
        [
            new("action", "save"),
            new("name", "A variant"),
            new("directions", "with a change"),
            new("quantity", "1 tin"),
            new("item", "an extra"),
            new("removed", ingredientId.ToString()),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);

        // The meal page lists the variant it now carries.
        var mealPage = await client.GetStringAsync($"/meals/{mealId}");
        Assert.Contains("A variant", mealPage);
        Assert.Contains("Adds 1 tin an extra", mealPage);
        Assert.Contains("Leaves out second", mealPage);
        Assert.Contains("Changes the directions", mealPage);

        int variantId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var variant = Assert.Single((await meals.GetAsync(mealId))!.Variants);
            variantId = variant.Id;
            Assert.Equal("with a change", variant.DirectionsModifications);
            Assert.Equal(ingredientId, Assert.Single(variant.RemovedIngredientIds));
        }

        var edited = await PostManyAsync(client, $"/meals/{mealId}/variants/{variantId}", "variant-editor",
        [
            new("action", "save"),
            new("name", "A renamed variant"),
            new("directions", string.Empty),
            new("item", "an extra"),
            new("quantity", "1 tin"),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var variant = Assert.Single((await meals.GetAsync(mealId))!.Variants);
            Assert.Equal("A renamed variant", variant.Name);
            Assert.Empty(variant.RemovedIngredientIds);
        }

        var deleted = await PostManyAsync(client, $"/meals/{mealId}/variants/{variantId}", "variant-delete", []);

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            Assert.Empty((await meals.GetAsync(mealId))!.Variants);
        }
    }

    [Fact]
    public async Task The_meal_filters_travel_in_the_query_string()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int tagId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();
            var tag = await tags.EnsureAsync(new MealTagRequest("quick", MealTagFacet.Effort));
            tagId = tag.Id;

            await meals.CreateAsync(new CreateMealRequest("A tagged meal", Status: MealStatus.Queued, TagIds: [tag.Id]));
            await meals.CreateAsync(new CreateMealRequest("A plain meal"));
        }

        Assert.Contains("A tagged meal", await client.GetStringAsync("/meals?text=tagged"));
        Assert.DoesNotContain("A plain meal", await client.GetStringAsync("/meals?text=tagged"));
        Assert.DoesNotContain("A plain meal", await client.GetStringAsync($"/meals?tag={tagId}"));
        Assert.DoesNotContain("A tagged meal", await client.GetStringAsync($"/meals?status={nameof(MealStatus.Closed)}"));
        Assert.Contains("A tagged meal", await client.GetStringAsync("/meals?sort=name-desc"));
    }

    [Fact]
    public async Task Meals_are_planned_and_sent_back_from_the_planning_page_without_a_circuit()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int mealId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = await meals.CreateAsync(new CreateMealRequest("A plain meal", Status: MealStatus.Available));
            mealId = meal.Id;
        }

        var saved = await PostManyAsync(client, "/meals/plan", "plan-targets",
        [
            new("breakfast", "1"),
            new("lunch", "0"),
            new("dinner", "2"),
            new("targets", "save"),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var page = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Breakfast 0 of 1", page);
        Assert.Contains("Dinner 0 of 2", page);
        Assert.Contains("A plain meal", page);

        var planned = await PostManyAsync(client, "/meals/plan", $"search-{mealId}",
        [
            new("meal-id", mealId.ToString()),
            new("slot", nameof(MealSlot.Dinner)),
            new("shared", "true"),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, planned.StatusCode);

        var afterPlanning = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Dinner 1 of 2", afterPlanning);
        Assert.Contains("Planned shared", afterPlanning);

        var moved = await PostManyAsync(client, "/meals/plan", $"move-{mealId}",
        [
            new("meal-id", mealId.ToString()),
            new("variant-id", string.Empty),
            new("slot", nameof(MealSlot.Lunch)),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, moved.StatusCode);

        var back = await PostManyAsync(client, "/meals/plan", $"back-{mealId}",
        [
            new("meal-id", mealId.ToString()),
            new("status", nameof(MealStatus.Available)),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = (await meals.GetAsync(mealId))!;

            Assert.Equal(MealStatus.Available, meal.Status);
            Assert.Null(meal.PlannedSlot);
            Assert.NotNull(meal.LastCookedAt);
        }
    }

    [Fact]
    public async Task The_meal_counts_are_still_there_after_leaving_the_planning_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostManyAsync(client, "/meals/plan", "plan-targets",
        [
            new("breakfast", "3"),
            new("lunch", "5"),
            new("dinner", "6"),
            new("targets", "save"),
        ]);

        await client.GetStringAsync("/meals");

        var returned = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Breakfast 0 of 3", returned);
        Assert.Contains("Lunch 0 of 5", returned);
        Assert.Contains("Dinner 0 of 6", returned);

        await PostManyAsync(client, "/meals/plan", "plan-targets", [new("targets", "reset")]);

        var afterReset = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Breakfast 0 of 4", afterReset);
        Assert.Contains("Lunch 0 of 2", afterReset);
        Assert.Contains("Dinner 0 of 7", afterReset);
    }

    [Fact]
    public async Task The_meals_link_is_not_lit_while_the_planning_page_is_open()
    {
        const string Lit = "class=\"nav-link active\" href=\"meals\"";

        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        Assert.Contains(Lit, await client.GetStringAsync("/meals"));

        var plan = await client.GetStringAsync("/meals/plan");
        Assert.DoesNotContain(Lit, plan);
        Assert.Contains("Meal Plan", plan);

        // A meal's own page still counts as the Meals section.
        Assert.Contains(Lit, await client.GetStringAsync("/meals/new"));
    }

    [Theory]
    [InlineData(MealSlot.Breakfast)]
    [InlineData(MealSlot.Lunch)]
    [InlineData(MealSlot.Dinner)]
    public async Task Every_slot_button_the_planning_page_offers_plans_into_that_slot(MealSlot slot)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int mealId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            mealId = (await meals.CreateAsync(new CreateMealRequest("A plain meal", Status: MealStatus.Available))).Id;
        }

        // Each button posts its own slot, which is the only thing that tells the page them apart.
        var page = await client.GetStringAsync("/meals/plan");
        Assert.Contains($"value=\"{slot}\"", page);

        var planned = await PostManyAsync(client, "/meals/plan", $"search-{mealId}",
        [
            new("meal-id", mealId.ToString()),
            new("slot", slot.ToString()),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, planned.StatusCode);

        using var scope2 = app.Services.CreateScope();
        var service = scope2.ServiceProvider.GetRequiredService<MealService>();
        var meal = (await service.GetAsync(mealId))!;

        Assert.Equal(slot, meal.PlannedSlot);
        Assert.Equal(MealStatus.Planned, meal.Status);

        // The slot it is already in is not offered again on its own row.
        Assert.Contains($"value=\"{slot}\" disabled", await client.GetStringAsync("/meals/plan"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_shared_box_on_a_planned_meal_is_saved_without_moving_it(bool shared)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int mealId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            mealId = (await meals.CreateAsync(new CreateMealRequest("A plain meal"))).Id;
            await meals.PlanAsync(mealId, MealSlot.Breakfast, shared: !shared);
        }

        // Save shared posts the box and no slot at all, which is the only way to turn the box on
        // or off for a meal whose own slot button is the disabled one.
        var fields = new List<KeyValuePair<string, string>>
        {
            new("meal-id", mealId.ToString()),
            new("variant-id", string.Empty),
            new("current-slot", nameof(MealSlot.Breakfast)),
            new("save-shared", "save"),
        };

        if (shared)
        {
            fields.Add(new("shared", "true"));
        }

        var saved = await PostManyAsync(client, "/meals/plan", $"move-{mealId}", fields);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using var after = app.Services.CreateScope();
        var service = after.ServiceProvider.GetRequiredService<MealService>();
        var meal = (await service.GetAsync(mealId))!;

        Assert.Equal(shared ? MealStatus.PlannedShared : MealStatus.Planned, meal.Status);
        Assert.Equal(MealSlot.Breakfast, meal.PlannedSlot);

        var page = await client.GetStringAsync("/meals/plan");
        Assert.Contains("name=\"save-shared\"", page);
        Assert.Equal(shared, page.Contains("name=\"shared\" value=\"true\" checked"));
    }

    [Fact]
    public async Task The_planning_page_counts_the_shared_meals_against_their_own_target()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        // Nothing stored yet: the slot defaults add up, and no shared meals are asked for.
        var fresh = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Planned 0 of 13", fresh);
        Assert.Contains("Shared 0 of 0", fresh);

        var saved = await PostManyAsync(client, "/meals/plan", "plan-targets",
        [
            new("breakfast", "1"),
            new("lunch", "2"),
            new("dinner", "3"),
            new("shared-target", "2"),
            new("targets", "save"),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var page = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Planned 0 of 6", page);
        Assert.Contains("Shared 0 of 2", page);
        Assert.Contains("id=\"target-shared\" type=\"number\" min=\"0\" name=\"shared-target\" value=\"2\"", page);

        int mealId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            mealId = (await meals.CreateAsync(new CreateMealRequest("A shared meal"))).Id;
            await meals.PlanAsync(mealId, MealSlot.Dinner, shared: true);
        }

        var counted = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Planned 1 of 6", counted);
        Assert.Contains("Shared 1 of 2", counted);
        Assert.DoesNotContain("more than the", counted);

        // The numbers survive a trip away from the page, like the slot targets do.
        await client.GetStringAsync("/meals");
        Assert.Contains("Shared 1 of 2", await client.GetStringAsync("/meals/plan"));

        await PostManyAsync(client, "/meals/plan", "plan-targets", [new("targets", "reset")]);
        var afterReset = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Planned 1 of 13", afterReset);
        Assert.Contains("Shared 1 of 0", afterReset);
    }

    [Fact]
    public async Task A_shared_target_above_the_meals_planned_for_is_refused_on_the_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var refused = await PostManyAsync(client, "/meals/plan", "plan-targets",
        [
            new("breakfast", "1"),
            new("lunch", "1"),
            new("dinner", "1"),
            new("shared-target", "4"),
            new("targets", "save"),
        ]);

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);

        var page = await refused.Content.ReadAsStringAsync();
        Assert.Contains("At most 3 of the planned meals can be shared ones.", page);

        // The refusal keeps what was typed, so the number to correct is still in the form, while the
        // counts below it go on reading the plan as it is still stored.
        Assert.Contains("name=\"shared-target\" value=\"4\"", page);
        Assert.Contains("name=\"dinner\" value=\"1\"", page);
        Assert.Contains("Planned 0 of 13", page);
        Assert.Contains("Shared 0 of 0", page);

        // Nothing was stored: the page comes back on the defaults.
        var reloaded = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Planned 0 of 13", reloaded);
        Assert.Contains("Shared 0 of 0", reloaded);
    }

    [Fact]
    public async Task More_shared_meals_than_the_plan_aims_for_is_called_out()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostManyAsync(client, "/meals/plan", "plan-targets",
        [
            new("breakfast", "0"),
            new("lunch", "0"),
            new("dinner", "1"),
            new("shared-target", "1"),
            new("targets", "save"),
        ]);

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            foreach (var name in new[] { "One", "Two", "Three" })
            {
                var meal = await meals.CreateAsync(new CreateMealRequest(name));
                await meals.PlanAsync(meal.Id, MealSlot.Dinner, shared: true);
            }
        }

        var page = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Planned 3 of 1", page);
        Assert.Contains("Shared 3 of 1", page);
        Assert.Contains("3 shared meals are planned, more than the 1 meal the plan aims for.", page);

        // Room for one more meal: the same line, counted and worded for more than one.
        await PostManyAsync(client, "/meals/plan", "plan-targets",
        [
            new("breakfast", "0"),
            new("lunch", "1"),
            new("dinner", "1"),
            new("shared-target", "1"),
            new("targets", "save"),
        ]);

        var raised = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Shared 3 of 1", raised);
        Assert.Contains("3 shared meals are planned, more than the 2 meals the plan aims for.", raised);

        // One more meal than that, and the line is gone.
        await PostManyAsync(client, "/meals/plan", "plan-targets",
        [
            new("breakfast", "1"),
            new("lunch", "1"),
            new("dinner", "1"),
            new("shared-target", "3"),
            new("targets", "save"),
        ]);

        var within = await client.GetStringAsync("/meals/plan");
        Assert.Contains("Shared 3 of 3", within);
        Assert.DoesNotContain("more than the", within);
    }

    [Fact]
    public async Task A_meal_with_variants_is_planned_through_the_chooser_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int mealId;
        int variantId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = await meals.CreateAsync(new CreateMealRequest("A meal with variants", Status: MealStatus.Queued));
            mealId = meal.Id;
            variantId = (await meals.CreateVariantAsync(mealId, new MealVariantRequest("A variant")))!.Id;
        }

        // The planning page sends a meal with variants to the chooser rather than planning it flat.
        var page = await client.GetStringAsync("/meals/plan");
        Assert.Contains($"meals/plan/choose/{mealId}", page);
        Assert.DoesNotContain("A meal with variants (A variant)", page);

        var chosen = await PostManyAsync(client, $"/meals/plan/choose/{mealId}", "plan-choice",
        [
            new("variant-id", variantId.ToString()),
            new("slot", nameof(MealSlot.Breakfast)),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, chosen.StatusCode);

        using var scope2 = app.Services.CreateScope();
        var service = scope2.ServiceProvider.GetRequiredService<MealService>();
        var planned = (await service.GetAsync(mealId))!;

        Assert.Equal(MealStatus.Planned, planned.Status);
        Assert.Equal(MealSlot.Breakfast, planned.PlannedSlot);
        Assert.Equal(variantId, planned.PlannedVariantId);
    }

    [Fact]
    public async Task An_imported_description_is_split_corrected_and_saved_without_a_circuit()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int mealId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = await meals.CreateAsync(new CreateMealRequest("An imported meal"));
            mealId = meal.Id;

            await meals.UpdateAsync(mealId, new UpdateMealRequest(
                ImportedText: "- 2 cups rice\n- 1 tin tomatoes\n\nSimmer it.",
                Directions: "- 2 cups rice\n- 1 tin tomatoes\n\nSimmer it.",
                NeedsIngredientSplit: true));
        }

        // The meal list points at the screen while anything is waiting.
        Assert.Contains("meals/split", await client.GetStringAsync("/meals"));

        var screen = await client.GetStringAsync("/meals/split");
        Assert.DoesNotContain("<!--Blazor:", screen);
        Assert.Contains("An imported meal", screen);
        Assert.Contains("2 cups", screen);
        Assert.Contains("rice", screen);
        Assert.Contains("Simmer it.", screen);

        // Clear throws the guess away and puts the imported text back.
        var cleared = await PostManyAsync(client, "/meals/split", "meal-split",
        [
            new("action", "clear"),
            new("meal-id", mealId.ToString()),
            new("quantity", "2 cups"),
            new("item", "rice"),
            new("directions", "Simmer it."),
        ]);

        var afterClear = await cleared.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Contains("- 2 cups rice", afterClear);

        var saved = await PostManyAsync(client, "/meals/split", "meal-split",
        [
            new("action", "save"),
            new("meal-id", mealId.ToString()),
            new("quantity", "2 cups"),
            new("item", "rice"),
            new("quantity", "1 tin"),
            new("item", "tomatoes"),
            new("quantity", string.Empty),
            new("item", string.Empty),
            new("directions", "Simmer it."),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        using var scope2 = app.Services.CreateScope();
        var service = scope2.ServiceProvider.GetRequiredService<MealService>();
        var split = (await service.GetAsync(mealId))!;

        Assert.False(split.NeedsIngredientSplit);
        Assert.Equal(["rice", "tomatoes"], split.Ingredients.Select(i => i.Item));
        Assert.Equal("Simmer it.", split.Directions);

        // The imported text is kept, so the split can always be started over.
        Assert.Contains("- 2 cups rice", split.ImportedText);
        Assert.Contains("Nothing is waiting", await client.GetStringAsync("/meals/split"));
    }

    [Fact]
    public async Task Skipping_a_meal_moves_on_to_the_next_one_waiting()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int first;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var one = await meals.CreateAsync(new CreateMealRequest("A first meal"));
            var two = await meals.CreateAsync(new CreateMealRequest("A second meal"));
            first = one.Id;

            foreach (var id in new[] { one.Id, two.Id })
            {
                await meals.UpdateAsync(id, new UpdateMealRequest(ImportedText: "Some text", NeedsIngredientSplit: true));
            }
        }

        var skipped = await PostManyAsync(client, "/meals/split", "meal-split",
        [
            new("action", "skip"),
            new("meal-id", first.ToString()),
            new("directions", "Some text"),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, skipped.StatusCode);
        Assert.Contains($"skipped={first}", skipped.Headers.Location!.OriginalString);

        var next = await client.GetStringAsync(skipped.Headers.Location!.OriginalString);
        Assert.Contains("A second meal", next);
        Assert.DoesNotContain("A first meal", next);
    }

    [Fact]
    public async Task An_automation_is_created_edited_and_deleted_without_a_circuit()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var created = await PostAsync(client, "/automations/new", "automation-editor", new()
        {
            ["form.Action"] = "save",
            ["form.Title"] = "Take the bins out",
            ["form.Notes"] = "Green bin this week",
            ["form.IntervalUnit"] = nameof(TaskIntervalUnit.Weeks),
            ["form.IntervalCount"] = "2",
            ["form.StartsOn"] = "2026-11-02",
            ["form.TimeOfDay"] = "07:30",
            ["form.DueInDays"] = "3",
            ["form.CategoryId"] = string.Empty,
            ["form.SkipWhenOpenTaskExists"] = "true",
            ["form.IsEnabled"] = "true",
        });

        // Saving a new automation goes back to the list, the same place saving an edit goes.
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/automations", created.Headers.Location!.AbsolutePath);

        int id;
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            var automation = await context.TaskAutomations.SingleAsync();
            id = automation.Id;

            Assert.Equal("Take the bins out", automation.Title);
            Assert.Equal("Green bin this week", automation.Notes);
            Assert.Equal(TaskIntervalUnit.Weeks, automation.IntervalUnit);
            Assert.Equal(2, automation.IntervalCount);
            Assert.Equal(new DateOnly(2026, 11, 2), automation.StartsOn);
            Assert.Equal(new TimeOnly(7, 30), automation.TimeOfDay);
            Assert.Equal(3, automation.DueInDays);
            Assert.True(automation.SkipWhenOpenTaskExists);
        }

        // The list shows it with the interval it keeps and the execution it is waiting for.
        var list = await client.GetStringAsync("/automations");
        Assert.Contains("Take the bins out", list);
        Assert.Contains("Every 2 weeks", list);
        Assert.Contains("Due 3 days after it is created", list);
        Assert.Contains("Never run", list);

        var edited = await PostAsync(client, $"/automations/{id}", "automation-editor", new()
        {
            ["form.Action"] = "save",
            ["form.Title"] = "Take the bins out and rinse them",
            ["form.Notes"] = string.Empty,
            ["form.IntervalUnit"] = nameof(TaskIntervalUnit.Months),
            ["form.IntervalCount"] = "9",
            ["form.StartsOn"] = "2026-12-01",
            ["form.TimeOfDay"] = "00:00",
            ["form.DueInDays"] = string.Empty,
            ["form.CategoryId"] = string.Empty,
            ["form.IsEnabled"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            var automation = await context.TaskAutomations.SingleAsync();

            Assert.Equal("Take the bins out and rinse them", automation.Title);
            Assert.Null(automation.Notes);
            Assert.Equal(TaskIntervalUnit.Months, automation.IntervalUnit);
            Assert.Equal(9, automation.IntervalCount);

            // An empty Due in box takes the due date off again rather than leaving the old one.
            Assert.Null(automation.DueInDays);

            // An unticked checkbox posts nothing, which has to read as off rather than as unset.
            Assert.False(automation.SkipWhenOpenTaskExists);
        }

        var deleted = await PostAsync(client, $"/automations/{id}", "automation-delete", []);

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            Assert.Empty(context.TaskAutomations);
        }
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
    public async Task The_preview_button_shows_every_intervals_executions_without_saving(TaskIntervalUnit unit, int count)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await PostAsync(client, "/automations/new", "automation-editor", new()
        {
            ["form.Action"] = "preview",
            ["form.Title"] = "Still being set up",
            ["form.IntervalUnit"] = unit.ToString(),
            ["form.IntervalCount"] = count.ToString(),
            ["form.StartsOn"] = "2026-11-02",
            ["form.TimeOfDay"] = "06:00",
            ["form.CategoryId"] = string.Empty,
            ["form.IsEnabled"] = "true",
        });

        var html = await posted.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Contains("Next executions", html);
        Assert.Contains("November 2, 2026 at 6:00 AM", html);

        // The typed title comes back with the page, and nothing was saved.
        Assert.Contains("Still being set up", html);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
        Assert.Empty(context.TaskAutomations);
    }

    [Fact]
    public async Task The_automation_list_says_its_piece_for_every_state_a_row_can_be_in()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        // Seeded through the context rather than the service, because the stamps a row's labels
        // read — when it was created, when it last ran — are not things a caller gets to set.
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            var longAgo = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

            context.TaskAutomations.AddRange(
                Row("No offset", longAgo, dueInDays: null),
                Row("Same day", longAgo, dueInDays: 0),
                Row("Next day", longAgo, dueInDays: 1),
                Row("A fortnight", longAgo, dueInDays: 14),
                Row("Paused one", longAgo, isEnabled: false),
                Row("Ran cleanly", longAgo, lastRunAt: longAgo.AddDays(1)),
                Row("Stood down", longAgo, lastRunAt: longAgo.AddDays(1), lastRunSkipped: true),
                // Counted yearly from the last day the calendar has, so there is no next one.
                Row("Off the end", new DateTimeOffset(9999, 12, 31, 23, 59, 0, TimeSpan.Zero),
                    startsOn: DateOnly.MaxValue),
                Row("Still to come", DateTimeOffset.UtcNow, startsOn: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)));

            await context.SaveChangesAsync();
        }

        var html = await client.GetStringAsync("/automations");

        // Every arm of the due-date label.
        Assert.Contains("No due date", html);
        Assert.Contains("Due the day it is created", html);
        Assert.Contains("Due the day after it is created", html);
        Assert.Contains("Due 14 days after it is created", html);

        // Paused shows both as a pill on the title line and as the row's next execution.
        Assert.Contains(">Paused</span>", html);
        Assert.Contains("Paused, nothing scheduled", html);

        // A schedule with nothing left, one that is overdue, and one still waiting.
        Assert.Contains("No further executions", html);
        Assert.Contains("Due, runs at the next check", html);
        Assert.Contains("Next ", html);

        // Never run, run, and run but stood down.
        Assert.Contains("Never run", html);
        Assert.Contains("Last run ", html);
        Assert.Contains(", skipped", html);
    }

    private static Domain.Tasks.TaskAutomation Row(
        string title,
        DateTimeOffset createdAt,
        DateOnly startsOn = default,
        int? dueInDays = null,
        bool isEnabled = true,
        DateTimeOffset? lastRunAt = null,
        bool lastRunSkipped = false) => new()
        {
            Title = title,
            IntervalUnit = startsOn == DateOnly.MaxValue ? TaskIntervalUnit.Years : TaskIntervalUnit.Days,
            IntervalCount = 1,
            StartsOn = startsOn == default ? DateOnly.FromDateTime(createdAt.UtcDateTime) : startsOn,
            DueInDays = dueInDays,
            IsEnabled = isEnabled,
            LastRunAt = lastRunAt,
            LastRunSkipped = lastRunSkipped,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    [Theory]
    [InlineData("Monday", DayOfWeek.Monday)]
    [InlineData("Tuesday", DayOfWeek.Tuesday)]
    [InlineData("Wednesday", DayOfWeek.Wednesday)]
    [InlineData("Thursday", DayOfWeek.Thursday)]
    [InlineData("Friday", DayOfWeek.Friday)]
    [InlineData("Saturday", DayOfWeek.Saturday)]
    [InlineData("Sunday", DayOfWeek.Sunday)]
    [InlineData("", DayOfWeek.Friday)]
    public async Task Every_day_of_the_week_can_be_picked_for_a_weekly_automation(string day, DayOfWeek runsOn)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        // The 30th of October 2026 is a Friday, so the empty option leaves the schedule there.
        var posted = await PostAsync(client, "/automations/new", "automation-editor", new()
        {
            ["form.Action"] = "save",
            ["form.Title"] = $"Chore for {(day.Length == 0 ? "no day" : day)}",
            ["form.IntervalUnit"] = nameof(TaskIntervalUnit.Weeks),
            ["form.IntervalCount"] = "2",
            ["form.StartsOn"] = "2026-10-30",
            ["form.WeekDay"] = day,
            ["form.TimeOfDay"] = "08:00",
            ["form.CategoryId"] = string.Empty,
            ["form.IsEnabled"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);

        int id;
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            var automation = await context.TaskAutomations.SingleAsync();
            id = automation.Id;

            Assert.Equal(day.Length == 0 ? null : Enum.Parse<DayOfWeek>(day), automation.WeekDay);
        }

        // Every date the editor previews falls on that day, whether it was picked or came from the
        // start date, which is a Friday.
        var editor = await client.GetStringAsync($"/automations/{id}");
        var previewed = Regex.Matches(editor, @"([A-Z][a-z]+ \d{1,2}, \d{4}) at")
            .Select(m => DateTime.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();

        Assert.Equal(TaskAutomationService.PreviewCount, previewed.Count);
        Assert.All(previewed, moment => Assert.Equal(runsOn, moment.DayOfWeek));

        var list = await client.GetStringAsync("/automations");
        Assert.Contains(day.Length == 0 ? "Every 2 weeks" : $"Every 2 weeks on {day}", list);
    }

    [Fact]
    public async Task A_day_of_the_week_cannot_be_picked_for_an_interval_that_is_not_weeks()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await PostAsync(client, "/automations/new", "automation-editor", new()
        {
            ["form.Action"] = "preview",
            ["form.Title"] = "Monthly",
            ["form.IntervalUnit"] = nameof(TaskIntervalUnit.Months),
            ["form.IntervalCount"] = "1",
            ["form.StartsOn"] = "2026-10-30",
            ["form.TimeOfDay"] = "08:00",
            ["form.CategoryId"] = string.Empty,
            ["form.IsEnabled"] = "true",
        });

        var html = await posted.Content.ReadAsStringAsync();
        var field = Regex.Match(html, """<select[^>]*id="automation-weekday"[^>]*>""").Value;

        Assert.Contains("disabled", field);
        Assert.Contains("weekly schedules only", html);
    }

    [Fact]
    public async Task A_due_date_of_zero_days_saves_and_reads_back_as_the_same_day()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await PostAsync(client, "/automations/new", "automation-editor", new()
        {
            ["form.Action"] = "save",
            ["form.Title"] = "Same day chore",
            ["form.IntervalUnit"] = nameof(TaskIntervalUnit.Weeks),
            ["form.IntervalCount"] = "1",
            ["form.StartsOn"] = "2026-11-02",
            ["form.TimeOfDay"] = "00:00",
            ["form.DueInDays"] = "0",
            ["form.CategoryId"] = string.Empty,
            ["form.IsEnabled"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);

        int id;
        using (var scope = app.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
            var automation = await context.TaskAutomations.SingleAsync();
            id = automation.Id;
            Assert.Equal(0, automation.DueInDays);
        }

        // Zero is a due date, not the empty box that means no due date, so the editor has to show
        // it back rather than read it as nothing set.
        var editor = await client.GetStringAsync($"/automations/{id}");
        var field = Regex.Match(editor, """<input[^>]*id="automation-due"[^>]*>""").Value;
        Assert.Contains("value=\"0\"", field);

        var list = await client.GetStringAsync("/automations");
        Assert.Contains("Due the day it is created", list);
    }

    [Fact]
    public async Task A_due_date_before_the_task_is_created_comes_back_as_an_error_on_the_editor()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await PostAsync(client, "/automations/new", "automation-editor", new()
        {
            ["form.Action"] = "save",
            ["form.Title"] = "Backwards",
            ["form.IntervalUnit"] = nameof(TaskIntervalUnit.Weeks),
            ["form.IntervalCount"] = "1",
            ["form.StartsOn"] = "2026-11-02",
            ["form.TimeOfDay"] = "00:00",
            ["form.DueInDays"] = "-1",
            ["form.CategoryId"] = string.Empty,
            ["form.IsEnabled"] = "true",
        });

        var html = await posted.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Contains("cannot be due before it is created", html);

        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CapybaraHomeDbContext>();
        Assert.Empty(context.TaskAutomations);
    }

    [Fact]
    public async Task A_blank_title_comes_back_as_an_error_on_the_editor()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var posted = await PostAsync(client, "/automations/new", "automation-editor", new()
        {
            ["form.Action"] = "save",
            ["form.Title"] = "   ",
            ["form.IntervalUnit"] = nameof(TaskIntervalUnit.Weeks),
            ["form.IntervalCount"] = "1",
            ["form.StartsOn"] = "2026-11-02",
            ["form.TimeOfDay"] = "00:00",
            ["form.CategoryId"] = string.Empty,
            ["form.IsEnabled"] = "true",
        });

        var html = await posted.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Contains("An automation needs a title", html);
    }

    [Fact]
    public async Task The_planning_page_lists_a_meal_once_however_many_variants_it_has()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var seeded = await SeedVariantAsync(app);

        var plan = await client.GetStringAsync("/meals/plan");

        Assert.Contains("A meal", plan);
        Assert.DoesNotContain("A meal (A variant)", plan);
        Assert.Contains($"meals/plan/choose/{seeded.MealId}", plan);
    }

    [Fact]
    public async Task A_planned_variant_links_to_the_variant_and_to_its_printable_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var seeded = await SeedVariantAsync(app);

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            await meals.PlanAsync(seeded.MealId, MealSlot.Dinner, seeded.VariantId);
        }

        var plan = await client.GetStringAsync("/meals/plan");

        Assert.Contains($"href=\"meals/{seeded.MealId}/variants/{seeded.VariantId}\"", plan);
        Assert.Contains($"href=\"meals/{seeded.MealId}/variants/{seeded.VariantId}/print\"", plan);
    }

    [Fact]
    public async Task The_chooser_and_the_meal_editor_spell_out_what_each_variant_changes()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var seeded = await SeedVariantAsync(app);

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            await meals.CreateVariantAsync(seeded.MealId, new MealVariantRequest("A plain variant"));
        }

        foreach (var path in new[] { $"/meals/plan/choose/{seeded.MealId}", $"/meals/{seeded.MealId}" })
        {
            var html = await client.GetStringAsync(path);

            Assert.Contains("Adds 2 cups walnuts", html);
            Assert.Contains("Leaves out 1 tin butter", html);
            Assert.Contains("Changes the directions", html);

            // The arm for a variant that holds nothing yet, which is what a half-filled one shows.
            Assert.Contains("Changes nothing yet", html);
        }
    }

    [Fact]
    public async Task The_printed_variant_carries_the_meals_directions_and_its_adjusted_ingredients()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);
        var seeded = await SeedVariantAsync(app);

        var meal = await client.GetStringAsync($"/meals/{seeded.MealId}/print");

        Assert.Contains("1 kg flour", meal);
        Assert.Contains("1 tin butter", meal);
        Assert.Contains("Mix it.", meal);
        Assert.Contains("A label", meal);

        var variant = await client.GetStringAsync($"/meals/{seeded.MealId}/variants/{seeded.VariantId}/print");

        Assert.Contains("A meal (A variant)", variant);
        Assert.Contains("1 kg flour", variant);
        Assert.Contains("2 cups walnuts", variant);
        Assert.DoesNotContain("butter", variant);
        Assert.Contains("Mix it.", variant);
        Assert.Contains("Bake it longer.", variant);
    }

    [Fact]
    public async Task A_meal_with_nothing_written_down_still_prints()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        int mealId;
        int variantId;
        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            mealId = (await meals.CreateAsync(new CreateMealRequest("A bare meal"))).Id;
            variantId = (await meals.CreateVariantAsync(mealId, new MealVariantRequest("A bare variant")))!.Id;
        }

        var meal = await client.GetStringAsync($"/meals/{mealId}/print");
        Assert.Contains("None listed.", meal);
        Assert.Contains("None written.", meal);

        var variant = await client.GetStringAsync($"/meals/{mealId}/variants/{variantId}/print");
        Assert.Contains("None listed.", variant);
        Assert.Contains("None written.", variant);
        Assert.DoesNotContain("Changes for this variant", variant);
    }

    // A meal with two ingredients, a tag and a variant that adds one ingredient, leaves one out
    // and changes the directions: enough for every arm the variant screens can show.
    private static async Task<(int MealId, int VariantId)> SeedVariantAsync(AppFactory app)
    {
        using var scope = app.Services.CreateScope();
        var meals = scope.ServiceProvider.GetRequiredService<MealService>();
        var tags = scope.ServiceProvider.GetRequiredService<MealTagService>();

        var tag = await tags.EnsureAsync(new MealTagRequest("A label", MealTagFacet.Trait));
        var meal = await meals.CreateAsync(new CreateMealRequest(
            "A meal",
            "Mix it.",
            MealStatus.Available,
            Ingredients: [new MealIngredientInput("flour", "1 kg"), new MealIngredientInput("butter", "1 tin")],
            TagIds: [tag.Id]));

        var butter = meal.Ingredients.Single(i => i.Item == "butter");
        var variant = await meals.CreateVariantAsync(meal.Id, new MealVariantRequest(
            "A variant", "Bake it longer.", [new MealIngredientInput("walnuts", "2 cups")], [butter.Id]));

        return (meal.Id, variant!.Id);
    }

    [Fact]
    public async Task A_grocery_list_is_generated_from_the_frequent_items_and_the_plan_without_a_circuit()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var frequent = await PostAsync(client, "/settings", "add-frequent", new()
        {
            ["frequentForm.Quantity"] = "2 pints",
            ["frequentForm.Item"] = "milk",
        });

        Assert.Equal(HttpStatusCode.Redirect, frequent.StatusCode);
        Assert.Contains("milk", await client.GetStringAsync("/settings"));

        using (var scope = app.Services.CreateScope())
        {
            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = await meals.CreateAsync(new CreateMealRequest("Curry", Ingredients:
                [new MealIngredientInput("rice", "2 cups"), new MealIngredientInput("tomatoes", "1 tin")]));
            await meals.PlanAsync(meal.Id, MealSlot.Dinner);
        }

        var prompts = await client.GetStringAsync("/grocery/generate");
        Assert.Contains("Frequent items", prompts);
        Assert.Contains("2 pints milk", prompts);
        Assert.Contains("Curry", prompts);
        Assert.Contains("2 cups rice", prompts);

        // The boxes name their own position, since an unticked box posts nothing at all.
        var added = await PostManyAsync(client, "/grocery/generate", "grocery-generate",
        [
            new("action", "add"),
            new("item", "milk"),
            new("quantity", "2 pints"),
            new("source", string.Empty),
            new("item", "rice"),
            new("quantity", "2 cups"),
            new("source", "Curry"),
            new("item", "tomatoes"),
            new("quantity", "1 tin"),
            new("source", "Curry"),
            new("pick", "0"),
            new("pick", "1"),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Contains("added=2", added.Headers.Location!.OriginalString);

        var list = await client.GetStringAsync("/grocery");
        Assert.Contains("Added 2 item(s)", await client.GetStringAsync(added.Headers.Location!.OriginalString));
        Assert.Contains("2 pints milk", list);
        Assert.Contains("2 cups rice", list);
        Assert.DoesNotContain("1 tin tomatoes", list);

        // What is on the list is marked instead of being offered a second time.
        var again = await client.GetStringAsync("/grocery/generate");
        Assert.Contains("already on the list", again);

        var typed = await PostManyAsync(client, "/grocery", "grocery-add",
        [
            new("quantity", string.Empty),
            new("item", "batteries"),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, typed.StatusCode);
        Assert.Contains("batteries", await client.GetStringAsync("/grocery"));

        var printed = await client.GetStringAsync("/grocery/print");
        Assert.Contains("2 pints milk", printed);
        Assert.Contains("batteries", printed);

        // Paper gets the list and nothing else: no navigation, no links to press.
        Assert.DoesNotContain("nav-bar", printed);

        int itemId;
        using (var scope = app.Services.CreateScope())
        {
            var grocery = scope.ServiceProvider.GetRequiredService<GroceryService>();
            var current = (await grocery.GetCurrentAsync())!;
            itemId = current.Items.Single(i => i.Item == "batteries").Id;
        }

        var dropped = await PostManyAsync(client, "/grocery", $"drop-{itemId}", []);

        Assert.Equal(HttpStatusCode.Redirect, dropped.StatusCode);
        Assert.DoesNotContain("batteries", await client.GetStringAsync("/grocery"));
    }

    [Fact]
    public async Task The_grocery_page_says_how_a_list_starts_before_there_is_one()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var page = await client.GetStringAsync("/grocery");

        Assert.Contains("No list has been started", page);

        // Nothing to start over from, so that control is not offered yet.
        Assert.DoesNotContain("Start a fresh list", page);

        await PostManyAsync(client, "/grocery", "grocery-add", [new("quantity", string.Empty), new("item", "milk")]);

        var started = await client.GetStringAsync("/grocery");

        Assert.DoesNotContain("No list has been started", started);
        Assert.Contains("Start a fresh list", started);
    }

    [Fact]
    public async Task A_fresh_list_is_started_from_the_page_and_the_old_one_stays_printable()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostManyAsync(client, "/grocery", "grocery-add", [new("quantity", string.Empty), new("item", "milk")]);

        var fresh = await PostManyAsync(client, "/grocery", "grocery-fresh", []);

        Assert.Equal(HttpStatusCode.Redirect, fresh.StatusCode);

        var page = await client.GetStringAsync("/grocery");
        Assert.Contains("Nothing on the list yet", page);
        Assert.Contains("Earlier lists", page);

        int closedId;
        using (var scope = app.Services.CreateScope())
        {
            var grocery = scope.ServiceProvider.GetRequiredService<GroceryService>();
            var lists = await grocery.ListAsync(includeClosed: true);
            closedId = lists.Single(l => l.ClosedAt is not null).Id;
        }

        Assert.Contains("milk", await client.GetStringAsync($"/grocery/print/{closedId}"));
        Assert.DoesNotContain("milk", await client.GetStringAsync("/grocery/print"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Every_box_the_generator_renders_adds_the_item_beside_it(int position)
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        using (var scope = app.Services.CreateScope())
        {
            var grocery = scope.ServiceProvider.GetRequiredService<GroceryService>();
            await grocery.AddFrequentAsync(new GroceryItemInput("milk", "2 pints"));

            var meals = scope.ServiceProvider.GetRequiredService<MealService>();
            var meal = await meals.CreateAsync(new CreateMealRequest("Curry", Ingredients:
                [new MealIngredientInput("rice", "2 cups"), new MealIngredientInput("tomatoes", "1 tin")]));
            await meals.PlanAsync(meal.Id, MealSlot.Dinner);
        }

        // Each box is rendered with the position it posts, so the page is the list of them.
        var prompts = await client.GetStringAsync("/grocery/generate");
        Assert.Contains($"value=\"{position}\"", prompts);

        var added = await PostManyAsync(client, "/grocery/generate", "grocery-generate",
        [
            new("action", "add"),
            new("item", "milk"),
            new("quantity", "2 pints"),
            new("source", string.Empty),
            new("item", "rice"),
            new("quantity", "2 cups"),
            new("source", "Curry"),
            new("item", "tomatoes"),
            new("quantity", "1 tin"),
            new("source", "Curry"),
            new("pick", position.ToString()),
        ]);

        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);

        using var scope2 = app.Services.CreateScope();
        var service = scope2.ServiceProvider.GetRequiredService<GroceryService>();
        var item = Assert.Single((await service.GetCurrentAsync())!.Items);

        Assert.Equal(new[] { "milk", "rice", "tomatoes" }[position], item.Item);
    }

    [Fact]
    public async Task The_generator_says_what_to_set_up_when_there_is_nothing_to_ask_about()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        var page = await client.GetStringAsync("/grocery/generate");

        Assert.Contains("Nothing to ask about", page);
        Assert.Contains("meals/plan", page);
    }

    [Fact]
    public async Task A_frequent_item_is_added_and_deleted_from_the_settings_page()
    {
        await using var app = await AppFactory.CreateAsync();
        var client = Client(app);

        await PostAsync(client, "/settings", "add-frequent", new()
        {
            ["frequentForm.Quantity"] = string.Empty,
            ["frequentForm.Item"] = "washing up liquid",
        });

        Assert.Contains("washing up liquid", await client.GetStringAsync("/settings"));

        int id;
        using (var scope = app.Services.CreateScope())
        {
            var grocery = scope.ServiceProvider.GetRequiredService<GroceryService>();
            id = Assert.Single(await grocery.ListFrequentAsync()).Id;
        }

        var deleted = await PostAsync(client, "/settings", $"delete-frequent-{id}", []);

        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.DoesNotContain("washing up liquid", await client.GetStringAsync("/settings"));
    }

    private static IEnumerable<string> Forms(string html)
    {
        foreach (var match in System.Text.RegularExpressions.Regex.Matches(html, "<form[\\s>].*?</form>",
            System.Text.RegularExpressions.RegexOptions.Singleline).ToList())
        {
            yield return match.Value;
        }
    }

    private static string PageHead(string html) => Between(html, "<div class=\"page-head\">", "</div>");

    private static string Nav(string html) => Between(html, "<nav class=\"nav-bar\"", "</nav>");

    private static string Between(string html, string open, string close)
    {
        var start = html.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The page has no {open} in it.");

        var end = html.IndexOf(close, start, StringComparison.Ordinal);
        Assert.True(end > start, $"The {open} on the page is never closed.");

        return html[start..end];
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

    // The meal editor posts repeated names, one pair per ingredient line, so its fields cannot be
    // carried in a dictionary the way the other forms' are.
    private static async Task<HttpResponseMessage> PostManyAsync(
        HttpClient client,
        string path,
        string handler,
        List<KeyValuePair<string, string>> fields)
    {
        fields.Add(new("_handler", handler));
        fields.Add(new("__RequestVerificationToken", await TokenAsync(client, path)));

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

    private static int Inputs(string html, string name) =>
        System.Text.RegularExpressions.Regex.Matches(html, $"name=\"{name}\"").Count;

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
