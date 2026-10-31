using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Dashboards;
using CapybaraHome.Application.Homes;
using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Application.Grocery;
using CapybaraHome.Application.Jira;
using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Application.Users;
using CapybaraHome.Domain.Homes;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Tasks;
using CapybaraHome.Web.Auth;

namespace CapybaraHome.Web.Endpoints;

/// <summary>The REST surface the UI and the connector share.</summary>
public static class ApiEndpoints
{
    /// <summary>Maps every endpoint under <c>/api</c>. All of them require a signed-in user or a token.</summary>
    public static void MapApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization(AuthenticationSetup.ApiPolicyName);

        MapTasks(api);
        MapAutomations(api);
        MapCategories(api);
        MapMeals(api);
        MapMealTags(api);
        MapProperties(api);
        MapHomeSearch(api);
        MapGrocery(api);
        MapDashboard(api);
        MapTransfer(api);
    }

    private static void MapTasks(IEndpointRouteBuilder api)
    {
        var tasks = api.MapGroup("/tasks");

        tasks.MapGet("/", async (
            TaskService service,
            string? text,
            TaskItemStatus? status,
            int? categoryId,
            bool? uncategorized,
            int? dueWithinDays,
            bool? overdue,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var filter = new TaskFilter(text, status is { } one ? [one] : null, categoryId, uncategorized ?? false, dueWithinDays, overdue ?? false, limit);
            return Results.Ok(await service.ListAsync(filter, cancellationToken: cancellationToken));
        });

        tasks.MapGet("/{id:int}", async (int id, TaskService service, CancellationToken cancellationToken) =>
            await service.GetAsync(id, cancellationToken) is { } task ? Results.Ok(task) : Results.NotFound());

        tasks.MapPost("/", async (CreateTaskRequest request, TaskService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/tasks/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        tasks.MapPatch("/{id:int}", async (int id, UpdateTaskRequest request, TaskService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        tasks.MapDelete("/{id:int}", async (int id, TaskService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapAutomations(IEndpointRouteBuilder api)
    {
        var automations = api.MapGroup("/automations");

        automations.MapGet("/", async (TaskAutomationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        automations.MapGet("/{id:int}", async (int id, TaskAutomationService service, CancellationToken cancellationToken) =>
            await service.GetAsync(id, cancellationToken) is { } automation ? Results.Ok(automation) : Results.NotFound());

        automations.MapPost("/", async (CreateTaskAutomationRequest request, TaskAutomationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/automations/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        automations.MapPatch("/{id:int}", async (int id, UpdateTaskAutomationRequest request, TaskAutomationService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        automations.MapDelete("/{id:int}", async (int id, TaskAutomationService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapMeals(IEndpointRouteBuilder api)
    {
        var meals = api.MapGroup("/meals");

        meals.MapGet("/", async (
            MealService service,
            string? text,
            MealStatus? status,
            int[]? tagId,
            bool? needsIngredientSplit,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var filter = new MealFilter(text, status is { } one ? [one] : null, tagId, needsIngredientSplit, limit);
            return Results.Ok(await service.ListAsync(filter, cancellationToken: cancellationToken));
        });

        meals.MapGet("/{id:int}", async (int id, MealService service, CancellationToken cancellationToken) =>
            await service.GetAsync(id, cancellationToken) is { } meal ? Results.Ok(meal) : Results.NotFound());

        meals.MapPost("/", async (CreateMealRequest request, MealService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/meals/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        meals.MapPatch("/{id:int}", async (int id, UpdateMealRequest request, MealService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        meals.MapDelete("/{id:int}", async (int id, MealService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        meals.MapPost("/{id:int}/variants", async (int id, MealVariantRequest request, MealService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.CreateVariantAsync(id, request, cancellationToken) is { } created
                    ? Results.Created($"/api/meals/{id}/variants/{created.Id}", created)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        meals.MapPut("/variants/{variantId:int}", async (int variantId, MealVariantRequest request, MealService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateVariantAsync(variantId, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        meals.MapDelete("/variants/{variantId:int}", async (int variantId, MealService service, CancellationToken cancellationToken) =>
            await service.DeleteVariantAsync(variantId, cancellationToken) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapMealTags(IEndpointRouteBuilder api)
    {
        var tags = api.MapGroup("/meal-tags");

        tags.MapGet("/", async (MealTagService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        tags.MapPost("/", async (MealTagRequest request, MealTagService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var tag = await service.EnsureAsync(request, cancellationToken);
                return Results.Created($"/api/meal-tags/{tag.Id}", tag);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        tags.MapPut("/{id:int}", async (int id, MealTagRequest request, MealTagService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        tags.MapDelete("/{id:int}", async (int id, MealTagService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapGrocery(IEndpointRouteBuilder api)
    {
        var grocery = api.MapGroup("/grocery");

        grocery.MapGet("/", async (GroceryService service, CancellationToken cancellationToken) =>
            await service.GetCurrentAsync(cancellationToken) is { } list ? Results.Ok(list) : Results.NoContent());

        grocery.MapGet("/lists", async (GroceryService service, bool? includeClosed, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(includeClosed ?? false, cancellationToken)));

        grocery.MapPost("/lists", async (GroceryService service, CancellationToken cancellationToken) =>
        {
            var list = await service.StartFreshAsync(cancellationToken);
            return Results.Created($"/api/grocery/lists/{list.Id}", list);
        });

        grocery.MapGet("/candidates", async (GroceryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CandidatesAsync(cancellationToken)));

        grocery.MapPost("/items", async (GroceryItemInput request, GroceryService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var added = await service.AddItemAsync(request, cancellationToken);
                return Results.Created($"/api/grocery/items/{added.Id}", added);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        grocery.MapDelete("/items/{id:int}", async (int id, GroceryService service, CancellationToken cancellationToken) =>
            await service.RemoveItemAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        grocery.MapGet("/frequent", async (GroceryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListFrequentAsync(cancellationToken)));

        grocery.MapPost("/frequent", async (GroceryItemInput request, GroceryService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var added = await service.AddFrequentAsync(request, cancellationToken);
                return Results.Created($"/api/grocery/frequent/{added.Id}", added);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        grocery.MapDelete("/frequent/{id:int}", async (int id, GroceryService service, CancellationToken cancellationToken) =>
            await service.RemoveFrequentAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapCategories(IEndpointRouteBuilder api)
    {
        var categories = api.MapGroup("/categories");

        categories.MapGet("/", async (CategoryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));

        categories.MapPost("/", async (CategoryRequest request, CategoryService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/categories/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        categories.MapPut("/{id:int}", async (int id, CategoryRequest request, CategoryService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        categories.MapDelete("/{id:int}", async (int id, CategoryService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapProperties(IEndpointRouteBuilder api)
    {
        var properties = api.MapGroup("/properties");

        properties.MapGet("/", async (HomeSearchBoardService board, CancellationToken cancellationToken) =>
            Results.Ok(await board.GetAsync(cancellationToken)));

        properties.MapGet("/{id:int}", async (int id, HomeSearchBoardService board, CancellationToken cancellationToken) =>
            await board.GetScoredAsync(id, cancellationToken) is { } scored ? Results.Ok(scored) : Results.NotFound());

        properties.MapPost("/", async (CreatePropertyRequest request, PropertyService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/properties/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        properties.MapPatch("/{id:int}", async (int id, UpdatePropertyRequest request, PropertyService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        properties.MapDelete("/{id:int}", async (int id, PropertyService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        properties.MapPost("/{id:int}/notes", async (
            int id,
            PropertyNoteRequest request,
            PropertyService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.AddNoteAsync(id, request.Body, cancellationToken) is { } note
                    ? Results.Created($"/api/properties/{id}/notes/{note.Id}", note)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        properties.MapDelete("/notes/{noteId:int}", async (int noteId, PropertyService service, CancellationToken cancellationToken) =>
            await service.DeleteNoteAsync(noteId, cancellationToken) ? Results.NoContent() : Results.NotFound());

        properties.MapPut("/{id:int}/priorities/{priorityId:int}", async (
            int id,
            int priorityId,
            PropertyPriorityRequest request,
            PropertyService service,
            CancellationToken cancellationToken) =>
            await service.MarkPriorityAsync(id, priorityId, request.Meets, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound());

        properties.MapPut("/{id:int}/drives/{destinationId:int}", async (
            int id,
            int destinationId,
            PropertyDriveTimeRequest request,
            PropertyService service,
            CancellationToken cancellationToken) =>
        {
            if (request.Minutes is null)
            {
                return await service.ClearDriveTimeAsync(id, destinationId, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound();
            }

            return await service.SetDriveTimeAsync(
                id, destinationId, request.Minutes.Value, request.Miles, DriveTimeSource.Manual, request.TolledRoads, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound();
        });

        properties.MapPost("/{id:int}/drives/measure", async (
            int id,
            DriveTimeMeasurer measurer,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return await measurer.MeasureAsync(id, cancellationToken) is { } outcome
                    ? Results.Ok(outcome)
                    : Results.NotFound();
            }
            catch (RoutingException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }

    private static void MapHomeSearch(IEndpointRouteBuilder api)
    {
        var search = api.MapGroup("/home-search");

        search.MapGet("/destinations", async (HomeSearchService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListDestinationsAsync(cancellationToken)));

        search.MapPost("/destinations", async (SearchDestinationRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateDestinationAsync(request, cancellationToken);
                return Results.Created($"/api/home-search/destinations/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        search.MapPut("/destinations/{id:int}", async (int id, SearchDestinationRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateDestinationAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        search.MapDelete("/destinations/{id:int}", async (int id, HomeSearchService service, CancellationToken cancellationToken) =>
            await service.DeleteDestinationAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        search.MapGet("/priorities", async (HomeSearchService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListPrioritiesAsync(cancellationToken)));

        search.MapPost("/priorities", async (SearchPriorityRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreatePriorityAsync(request, cancellationToken);
                return Results.Created($"/api/home-search/priorities/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        search.MapPut("/priorities/{id:int}", async (int id, SearchPriorityRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdatePriorityAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        search.MapDelete("/priorities/{id:int}", async (int id, HomeSearchService service, CancellationToken cancellationToken) =>
            await service.DeletePriorityAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        search.MapGet("/toll-roads", async (HomeSearchService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListTollRoadsAsync(cancellationToken)));

        search.MapPost("/toll-roads", async (TollRoadRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateTollRoadAsync(request, cancellationToken);
                return Results.Created($"/api/home-search/toll-roads/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        search.MapPut("/toll-roads/{id:int}", async (int id, TollRoadRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateTollRoadAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        search.MapDelete("/toll-roads/{id:int}", async (int id, HomeSearchService service, CancellationToken cancellationToken) =>
            await service.DeleteTollRoadAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        search.MapGet("/vehicles", async (HomeSearchService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListVehiclesAsync(cancellationToken)));

        search.MapPost("/vehicles", async (VehicleRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateVehicleAsync(request, cancellationToken);
                return Results.Created($"/api/home-search/vehicles/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        search.MapPut("/vehicles/{id:int}", async (int id, VehicleRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateVehicleAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        search.MapDelete("/vehicles/{id:int}", async (int id, HomeSearchService service, CancellationToken cancellationToken) =>
            await service.DeleteVehicleAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        // The routing key is write-only: the settings say whether one is held, never what it is.
        search.MapGet("/settings", async (HomeSearchService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetSettingsAsync(cancellationToken)));

        search.MapPut("/settings", async (HomeSearchSettingsRequest request, HomeSearchService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.SaveSettingsAsync(request, cancellationToken)));
    }

    private static void MapDashboard(IEndpointRouteBuilder api)
    {
        var dashboard = api.MapGroup("/dashboard/cards");

        dashboard.MapGet("/", async (DashboardService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetDashboardAsync(cancellationToken)));

        dashboard.MapPost("/", async (DashboardCardRequest request, DashboardService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/dashboard/cards/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        dashboard.MapPut("/{id:int}", async (int id, DashboardCardRequest request, DashboardService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return await service.UpdateAsync(id, request, cancellationToken) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        dashboard.MapDelete("/{id:int}", async (int id, DashboardService service, CancellationToken cancellationToken) =>
            await service.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

        dashboard.MapPost("/order", async (int[] orderedIds, DashboardService service, CancellationToken cancellationToken) =>
        {
            await service.ReorderAsync(orderedIds, cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapTransfer(IEndpointRouteBuilder api)
    {
        api.MapGet("/export", async (DataTransferService service, CancellationToken cancellationToken) =>
        {
            var json = await service.ExportToJsonAsync(cancellationToken);
            var name = $"capybarahome-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", name);
        });

        api.MapPost("/import", async (
            HttpRequest request,
            DataTransferService service,
            ImportMode? mode,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);

            try
            {
                return Results.Ok(await service.ImportFromJsonAsync(json, mode ?? ImportMode.Merge, cancellationToken));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapPost("/import/jira", async (
            HttpRequest request,
            JiraImportService service,
            CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);

            try
            {
                return Results.Ok(await service.ImportAsync(json, cancellationToken));
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapPost("/backups", async (BackupWriter writer, CancellationToken cancellationToken) =>
        {
            var path = await writer.WriteAsync(cancellationToken);
            return Results.Ok(new { path });
        });

        api.MapGet("/me", (HttpContext http, CancellationToken cancellationToken) =>
            Results.Ok(new
            {
                userId = http.User.GetUserId(),
                name = http.User.Identity?.Name,
            }));

        api.MapGet("/users", async (UserService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));
    }
}
