using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Grocery;
using CapybaraHome.Application.Homes;
using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Tasks;
using CapybaraHome.Web.Mcp;
using Microsoft.AspNetCore.Http;

namespace CapybaraHome.Tests;

/// <summary>
/// Exercises every MCP tool and the audit line it writes, which has to name the user the
/// request's token resolved to rather than the endpoint it arrived on.
/// </summary>
public class McpToolTests
{
    [Fact]
    public async Task Every_task_tool_answers_and_names_the_caller()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tasks = new TaskService(db.Context, db.Clock);
        var categories = new CategoryService(db.Context);
        var log = new McpAuditLog<TaskTools>();
        var http = McpAuditLog<TaskTools>.RequestFrom("first");

        var created = await TaskTools.CreateTaskAsync(tasks, log, http, "Change the filter", notes: "hall closet");
        Assert.Contains("[MCP] create_task user=first title=Change the filter", log.Single());

        log.Clear();
        var read = await TaskTools.GetTaskAsync(tasks, log, http, created.Id);
        Assert.Equal("Change the filter", read!.Title);
        Assert.Contains($"[MCP] get_task user=first taskId={created.Id}", log.Single());

        log.Clear();
        var updated = await TaskTools.UpdateTaskAsync(tasks, log, http, created.Id, status: TaskItemStatus.InProgress);
        Assert.Equal(TaskItemStatus.InProgress, updated!.Status);
        Assert.Contains("[MCP] update_task user=first", log.Single());

        log.Clear();
        var found = await TaskTools.SearchTasksAsync(tasks, log, http, text: "filter");
        Assert.Equal(created.Id, Assert.Single(found).Id);
        Assert.Contains("[MCP] search_tasks user=first text=filter", log.Single());

        log.Clear();
        Assert.Empty(await TaskTools.ListCategoriesAsync(categories, log, http));
        Assert.Contains("[MCP] list_categories user=first", log.Single());
    }

    [Fact]
    public async Task Every_meal_tool_answers_and_names_the_caller()
    {
        await using var db = await TestDatabase.CreateAsync();
        var meals = new MealService(db.Context, db.Clock);
        var tags = new MealTagService(db.Context);
        var log = new McpAuditLog<MealTools>();
        var http = McpAuditLog<MealTools>.RequestFrom("first");

        var created = await MealTools.CreateMealAsync(
            meals,
            log,
            http,
            "Katsu curry",
            directions: "Fry the cutlet.",
            ingredients: [new MealIngredientInput("pork loin", "2 cuts")]);
        Assert.Contains("[MCP] create_meal user=first name=Katsu curry", log.Single());

        log.Clear();
        var read = await MealTools.GetMealAsync(meals, log, http, created.Id);
        Assert.Equal("Katsu curry", read!.Name);
        Assert.Contains($"[MCP] get_meal user=first mealId={created.Id}", log.Single());

        log.Clear();
        var updated = await MealTools.UpdateMealAsync(meals, log, http, created.Id, status: MealStatus.Available);
        Assert.Equal(MealStatus.Available, updated!.Status);
        Assert.Contains("[MCP] update_meal user=first", log.Single());

        log.Clear();
        var variant = await MealTools.CreateMealVariantAsync(
            meals,
            log,
            http,
            created.Id,
            "Chicken katsu",
            addedIngredients: [new MealIngredientInput("chicken thigh")]);
        Assert.Equal("Chicken katsu", variant!.Name);
        Assert.Contains("[MCP] create_meal_variant user=first", log.Single());

        log.Clear();
        var found = await MealTools.SearchMealsAsync(meals, log, http, text: "katsu");
        Assert.Equal(created.Id, Assert.Single(found).Id);
        Assert.Contains("[MCP] search_meals user=first text=katsu", log.Single());

        log.Clear();
        await tags.EnsureAsync(new MealTagRequest("quick"));
        Assert.Equal("quick", Assert.Single(await MealTools.ListMealTagsAsync(tags, log, http)).Name);
        Assert.Contains("[MCP] list_meal_tags user=first", log.Single());
    }

    [Fact]
    public async Task Every_grocery_tool_answers_and_names_the_caller()
    {
        await using var db = await TestDatabase.CreateAsync();
        var grocery = new GroceryService(db.Context, db.Clock);
        var log = new McpAuditLog<GroceryTools>();
        var http = McpAuditLog<GroceryTools>.RequestFrom("first");

        Assert.Null(await GroceryTools.GetGroceryListAsync(grocery, log, http));
        Assert.Contains("[MCP] get_grocery_list user=first", log.Single());

        log.Clear();
        var item = await GroceryTools.AddGroceryItemAsync(grocery, log, http, "milk", "2 pints");
        Assert.Contains("[MCP] add_grocery_item user=first item=milk quantity=2 pints", log.Single());

        log.Clear();
        var list = await GroceryTools.GetGroceryListAsync(grocery, log, http);
        Assert.Equal("milk", Assert.Single(list!.Items).Item);

        log.Clear();
        Assert.True(await GroceryTools.RemoveGroceryItemAsync(grocery, log, http, item.Id));
        Assert.Contains($"[MCP] remove_grocery_item user=first itemId={item.Id}", log.Single());

        log.Clear();
        Assert.Empty((await GroceryTools.StartFreshGroceryListAsync(grocery, log, http)).Items);
        Assert.Contains("[MCP] start_fresh_grocery_list user=first", log.Single());

        log.Clear();
        var candidates = await GroceryTools.ListGroceryCandidatesAsync(grocery, log, http);
        Assert.Equal(0, candidates.Offered);
        Assert.Contains("[MCP] list_grocery_candidates user=first", log.Single());
    }

    [Fact]
    public async Task Every_home_search_tool_answers_and_names_the_caller()
    {
        await using var db = await TestDatabase.CreateAsync();
        var properties = new PropertyService(db.Context, db.Clock);
        var board = new HomeSearchBoardService(properties, new HomeSearchService(db.Context, db.Clock));
        var log = new McpAuditLog<HomeSearchTools>();
        var http = McpAuditLog<HomeSearchTools>.RequestFrom("first");

        var added = await HomeSearchTools.AddPropertyAsync(
            properties,
            log,
            http,
            "Maple Court",
            address: "1 Maple Court",
            rent: 2400m);
        Assert.Contains("[MCP] add_property user=first name=Maple Court rent=2400", log.Single());

        log.Clear();
        var note = await HomeSearchTools.AddPropertyNoteAsync(properties, log, http, added.Id, "Loud road.");
        Assert.Equal("Loud road.", note!.Body);
        Assert.Contains($"[MCP] add_property_note user=first propertyId={added.Id} noteLength=10", log.Single());

        log.Clear();
        var scored = await HomeSearchTools.GetPropertyAsync(board, log, http, added.Id);
        Assert.Equal("Maple Court", scored!.Property.Name);
        Assert.Contains($"[MCP] get_property user=first propertyId={added.Id}", log.Single());

        log.Clear();
        var search = await HomeSearchTools.GetHomeSearchAsync(board, log, http);
        Assert.Equal(added.Id, Assert.Single(search.Candidates).Property.Id);
        Assert.Contains("[MCP] get_home_search user=first", log.Single());
    }

    [Fact]
    public void A_call_with_no_request_behind_it_is_logged_as_unknown()
    {
        Assert.Equal("unknown", new HttpContextAccessor().CurrentUser());
        Assert.Equal("unknown", new HttpContextAccessor { HttpContext = new DefaultHttpContext() }.CurrentUser());
    }
}
