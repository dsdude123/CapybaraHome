using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Application.Categories;
using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Application.Tasks;
using CapybaraHome.Domain.Tasks;

namespace CapybaraHome.Tests;

/// <summary>Covers the export and import that back the automatic backups.</summary>
public class DataTransferServiceTests
{
    [Fact]
    public async Task An_export_imported_into_an_empty_database_restores_what_was_there()
    {
        await using var source = await TestDatabase.CreateAsync();
        var tasks = source.Tasks();
        var categories = new CategoryService(source.Context);
        var category = await categories.CreateAsync(new CategoryRequest("Garage", "#A8D5BA", 3));
        await tasks.CreateAsync(new CreateTaskRequest("Clear the shelves", "with the ladder", TaskItemStatus.InProgress, category.Id, new DateOnly(2026, 12, 1)));

        var json = await new DataTransferService(source.Context, source.Clock).ExportToJsonAsync();

        await using var target = await TestDatabase.CreateAsync();
        var summary = await new DataTransferService(target.Context, target.Clock).ImportFromJsonAsync(json, ImportMode.Merge);

        Assert.Equal(1, summary.CategoriesAdded);
        Assert.Equal(1, summary.TasksAdded);

        var restored = await target.Tasks().ListAsync(new TaskFilter());
        var restoredTask = Assert.Single(restored);
        Assert.Equal("Clear the shelves", restoredTask.Title);
        Assert.Equal("with the ladder", restoredTask.Notes);
        Assert.Equal(TaskItemStatus.InProgress, restoredTask.Status);
        Assert.Equal(new DateOnly(2026, 12, 1), restoredTask.DueDate);
        Assert.Equal("Garage", restoredTask.CategoryName);
    }

    [Fact]
    public async Task A_task_carries_its_close_operations_through_an_export_and_back()
    {
        await using var source = await TestDatabase.CreateAsync();
        var task = await source.Tasks().CreateAsync(new CreateTaskRequest("Bleed the radiators"));
        var operations = source.CloseOperations();

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.CreateTask,
            FollowUpTitle: "Bleed them again",
            DueDateMode: TaskDueDateMode.InMonths,
            DueDateValue: 12,
            CarryOperationsForward: false));

        await operations.CreateAsync(task.Id, new CreateTaskCloseOperationRequest(
            TaskCloseOperationKind.Webhook,
            WebhookUrl: "https://hooks.example.com/radiators",
            WebhookMethod: TaskWebhookMethod.Get,
            IsEnabled: false));

        var json = await new DataTransferService(source.Context, source.Clock).ExportToJsonAsync();

        await using var target = await TestDatabase.CreateAsync();
        await new DataTransferService(target.Context, target.Clock).ImportFromJsonAsync(json, ImportMode.Merge);

        var restoredTask = Assert.Single(await target.Tasks().ListAsync(new TaskFilter()));
        var restored = await target.CloseOperations().ListAsync(restoredTask.Id);

        Assert.Equal(2, restored.Count);

        var followUp = Assert.Single(restored, o => o.Kind == TaskCloseOperationKind.CreateTask);
        Assert.Equal("Bleed them again", followUp.FollowUpTitle);
        Assert.Equal(TaskDueDateMode.InMonths, followUp.DueDateMode);
        Assert.Equal(12, followUp.DueDateValue);
        Assert.False(followUp.CarryOperationsForward);

        var webhook = Assert.Single(restored, o => o.Kind == TaskCloseOperationKind.Webhook);
        Assert.Equal("https://hooks.example.com/radiators", webhook.WebhookUrl);
        Assert.Equal(TaskWebhookMethod.Get, webhook.WebhookMethod);
        Assert.False(webhook.IsEnabled);
    }

    [Fact]
    public async Task A_merge_updates_the_tasks_it_can_match_on_their_jira_key()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);
        db.Context.Tasks.Add(new TaskItem { Title = "Old title", JiraKey = "CAPY-1" });
        await db.Context.SaveChangesAsync();

        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            db.Clock.GetUtcNow(),
            [],
            [new TaskRecord(1, "New title", null, null, TaskItemStatus.Done, null, null, "CAPY-1", db.Clock.GetUtcNow(), db.Clock.GetUtcNow())],
            []);

        var summary = await transfer.ImportAsync(document, ImportMode.Merge);

        Assert.Equal(1, summary.TasksUpdated);
        Assert.Equal(0, summary.TasksAdded);
        Assert.Equal("New title", db.Context.Tasks.Single().Title);
    }

    [Fact]
    public async Task Replacing_clears_what_was_there_first()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);
        db.Context.Tasks.Add(new TaskItem { Title = "Should not survive" });
        await db.Context.SaveChangesAsync();

        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            db.Clock.GetUtcNow(),
            [],
            [new TaskRecord(1, "The only task", null, null, TaskItemStatus.ToDo, null, null, null, db.Clock.GetUtcNow(), db.Clock.GetUtcNow())],
            []);

        await transfer.ImportAsync(document, ImportMode.Replace);

        Assert.Equal("The only task", db.Context.Tasks.Single().Title);
    }

    [Fact]
    public async Task A_document_from_a_later_format_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);

        var json = System.Text.Json.JsonSerializer.Serialize(
            new BackupDocument(99, db.Clock.GetUtcNow(), [], [], []),
            DataTransferService.JsonOptions);

        await Assert.ThrowsAsync<InvalidOperationException>(() => transfer.ImportFromJsonAsync(json, ImportMode.Merge));
    }

    [Fact]
    public async Task A_backup_writes_a_file_and_keeps_only_the_newest_ones()
    {
        await using var db = await TestDatabase.CreateAsync();
        var destination = Directory.CreateTempSubdirectory("capybarahome-tests");

        try
        {
            var options = Microsoft.Extensions.Options.Options.Create(new BackupOptions
            {
                Enabled = true,
                Destination = destination.FullName,
                Keep = 2,
            });

            var writer = new BackupWriter(new DataTransferService(db.Context, db.Clock), options, db.Clock);

            for (var i = 0; i < 3; i++)
            {
                await writer.WriteAsync();
                db.Clock.Advance(TimeSpan.FromMinutes(1));
            }

            Assert.Equal(2, destination.GetFiles("capybarahome-*.json").Length);
        }
        finally
        {
            destination.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_meal_survives_a_round_trip_with_its_ingredients_and_tags()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);
        var tags = new MealTagService(db.Context);
        var meals = new MealService(db.Context, db.Clock);
        var tag = await tags.EnsureAsync(new MealTagRequest("quick", MealTagFacet.Effort));
        await meals.CreateAsync(new CreateMealRequest(
            "A meal", "Cook it", MealStatus.Queued, false, new DateOnly(2026, 9, 1),
            [new MealIngredientInput("an item", "2 cups")], [tag.Id]));

        var json = await transfer.ExportToJsonAsync();

        await using var restored = await TestDatabase.CreateAsync();
        var into = new DataTransferService(restored.Context, restored.Clock);
        var summary = await into.ImportFromJsonAsync(json, ImportMode.Merge);

        Assert.Equal(1, summary.MealsAdded);

        var meal = await new MealService(restored.Context, restored.Clock).ListAsync(new MealFilter());
        var only = Assert.Single(meal);
        Assert.Equal("A meal", only.Name);
        Assert.Equal(MealStatus.Queued, only.Status);
        Assert.Equal(new DateOnly(2026, 9, 1), only.LastCookedAt);
        Assert.Equal("2 cups", Assert.Single(only.Ingredients).Quantity);
        Assert.Equal("quick", Assert.Single(only.Tags).Name);
    }

    [Fact]
    public async Task A_merge_updates_the_meals_it_can_match_on_their_jira_key()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);
        db.Context.Meals.Add(new Meal { Name = "Old name", JiraKey = "FOOD-1" });
        await db.Context.SaveChangesAsync();

        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            db.Clock.GetUtcNow(),
            [],
            [],
            [],
            [new MealRecord(1, "New name", null, MealStatus.Available, false, null, null, false, "FOOD-1", [], [], [], db.Clock.GetUtcNow(), db.Clock.GetUtcNow())],
            []);

        var summary = await transfer.ImportAsync(document, ImportMode.Merge);

        Assert.Equal(1, summary.MealsUpdated);
        Assert.Equal(0, summary.MealsAdded);
        Assert.Equal("New name", db.Context.Meals.Single().Name);
    }

    [Fact]
    public async Task An_export_from_before_meals_existed_still_reads()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);

        var json = """
        {
          "formatVersion": 1,
          "exportedAt": "2026-10-22T00:00:00+00:00",
          "categories": [],
          "tasks": [ { "id": 1, "title": "A restored task", "status": "ToDo",
            "createdAt": "2026-10-01T00:00:00+00:00", "updatedAt": "2026-10-01T00:00:00+00:00" } ],
          "dashboardCards": []
        }
        """;

        var summary = await transfer.ImportFromJsonAsync(json, ImportMode.Merge);

        Assert.Equal(1, summary.TasksAdded);
        Assert.Equal(0, summary.MealsAdded);
        Assert.Equal("A restored task", db.Context.Tasks.Single().Title);
    }

    [Fact]
    public async Task Replacing_clears_the_meals_that_were_there_first()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);
        db.Context.Meals.Add(new Meal { Name = "Should not survive" });
        await db.Context.SaveChangesAsync();

        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            db.Clock.GetUtcNow(),
            [],
            [],
            [],
            [new MealRecord(1, "The only meal", null, MealStatus.Pooled, false, null, null, false, null, [], [], [], db.Clock.GetUtcNow(), db.Clock.GetUtcNow())],
            []);

        await transfer.ImportAsync(document, ImportMode.Replace);

        Assert.Equal("The only meal", db.Context.Meals.Single().Name);
    }

    [Fact]
    public async Task A_variant_survives_a_round_trip_and_still_leaves_out_the_right_ingredient()
    {
        await using var db = await TestDatabase.CreateAsync();
        var transfer = new DataTransferService(db.Context, db.Clock);
        var meals = new MealService(db.Context, db.Clock);
        var meal = await meals.CreateAsync(new CreateMealRequest("A meal", Ingredients:
            [new MealIngredientInput("first"), new MealIngredientInput("second")]));
        await meals.CreateVariantAsync(meal.Id, new MealVariantRequest(
            "A variant", "with a change", [new MealIngredientInput("an extra", "1 tin")], [meal.Ingredients[1].Id]));

        var json = await transfer.ExportToJsonAsync();

        await using var restored = await TestDatabase.CreateAsync();
        var into = new DataTransferService(restored.Context, restored.Clock);
        await into.ImportFromJsonAsync(json, ImportMode.Merge);

        var service = new MealService(restored.Context, restored.Clock);
        var loaded = Assert.Single(await service.ListAsync(new MealFilter()));
        var variant = Assert.Single(loaded.Variants);

        Assert.Equal("A variant", variant.Name);
        Assert.Equal("an extra", Assert.Single(variant.AddedIngredients).Item);
        Assert.Equal(loaded.Ingredients.Single(i => i.Item == "second").Id, Assert.Single(variant.RemovedIngredientIds));
    }

    [Fact]
    public async Task An_export_carries_the_automations_across_and_a_second_merge_does_not_double_them()
    {
        await using var source = await TestDatabase.CreateAsync();
        var categories = new CategoryService(source.Context);
        var category = await categories.CreateAsync(new CategoryRequest("Garden"));
        var automations = new TaskAutomationService(
            source.Context, source.Tasks(), source.Clock);

        await automations.CreateAsync(new CreateTaskAutomationRequest(
            "Water the tomatoes",
            "The ones in the greenhouse",
            TaskAutomationFrequency.BiWeekly,
            new DateOnly(2026, 11, 2),
            new TimeOnly(7, 30),
            category.Id,
            SkipWhenOpenTaskExists: true,
            IsEnabled: false));

        var json = await new DataTransferService(source.Context, source.Clock).ExportToJsonAsync();

        await using var target = await TestDatabase.CreateAsync();
        var into = new DataTransferService(target.Context, target.Clock);

        var first = await into.ImportFromJsonAsync(json, ImportMode.Merge);
        Assert.Equal(1, first.AutomationsAdded);

        var restored = Assert.Single(await new TaskAutomationService(
            target.Context, target.Tasks(), target.Clock).ListAsync());

        Assert.Equal("Water the tomatoes", restored.Title);
        Assert.Equal("The ones in the greenhouse", restored.Notes);
        Assert.Equal(TaskAutomationFrequency.BiWeekly, restored.Frequency);
        Assert.Equal(new DateOnly(2026, 11, 2), restored.StartsOn);
        Assert.Equal(new TimeOnly(7, 30), restored.TimeOfDay);
        Assert.True(restored.SkipWhenOpenTaskExists);
        Assert.False(restored.IsEnabled);
        Assert.Equal("Garden", restored.CategoryName);

        var second = await into.ImportFromJsonAsync(json, ImportMode.Merge);
        Assert.Equal(0, second.AutomationsAdded);
        Assert.Equal(1, second.AutomationsUpdated);
        Assert.Single(target.Context.TaskAutomations);
    }

}
