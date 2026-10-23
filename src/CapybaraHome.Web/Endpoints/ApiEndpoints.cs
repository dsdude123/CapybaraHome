using CapybaraHome.Application.Categories;
using CapybaraHome.Application.Dashboards;
using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Application.Jira;
using CapybaraHome.Application.Meals;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Application.Users;
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
        MapCategories(api);
        MapMeals(api);
        MapMealTags(api);
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
