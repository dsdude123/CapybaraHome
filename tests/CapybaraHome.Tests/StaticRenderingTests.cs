using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CapybaraHome.Application.Jira;
using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Tasks;
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
    [InlineData("/automations")]
    [InlineData("/automations/new")]
    [InlineData("/meals")]
    [InlineData("/meals/new")]
    [InlineData("/meals/plan")]
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
            new("quantity", string.Empty),
            new("item", string.Empty),
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
            new("action", "add-rows"),
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
        Assert.Contains("1 added", mealPage);
        Assert.Contains("1 removed", mealPage);

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

        var planned = await PostManyAsync(client, "/meals/plan", $"search-{mealId}-base",
        [
            new("meal-id", mealId.ToString()),
            new("variant-id", string.Empty),
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

        var planned = await PostManyAsync(client, "/meals/plan", $"search-{mealId}-base",
        [
            new("meal-id", mealId.ToString()),
            new("variant-id", string.Empty),
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
        Assert.Contains("A meal with variants (A variant)", page);

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

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var editor = created.Headers.Location!.OriginalString;

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

        var edited = await PostAsync(client, editor, "automation-editor", new()
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
