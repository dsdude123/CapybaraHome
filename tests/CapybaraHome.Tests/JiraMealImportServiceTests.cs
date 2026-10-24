using System.Text.Json;
using CapybaraHome.Application.Jira;
using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>Covers the straight copy of a JIRA food board into meals.</summary>
public class JiraMealImportServiceTests
{
    private const string Board = """
    { "issues": [
        { "key": "FOOD-1", "fields": {
            "summary": "A meal",
            "status": { "name": "Available" },
            "labels": [ "quick", "oven" ],
            "description": { "type": "doc", "version": 1, "content": [
                { "type": "paragraph", "content": [ { "type": "text", "text": "Two cups of rice. Simmer it." } ] } ] },
            "created": "2026-01-01T00:00:00.000+0000",
            "updated": "2026-06-01T00:00:00.000+0000" } },
        { "key": "FOOD-2", "fields": {
            "summary": "[One-Time] Another meal",
            "status": { "name": "Planned Shared" },
            "labels": [],
            "description": { "type": "doc", "version": 1, "content": [] },
            "created": "2026-02-01T00:00:00.000+0000",
            "updated": "2026-07-01T00:00:00.000+0000" } } ] }
    """;

    private static List<JsonElement> Issues(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JiraFields.Issues(document.RootElement.Clone()).ToList();
    }

    [Fact]
    public async Task Every_issue_becomes_a_meal_with_its_description_carried_across_whole()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);

        var report = await service.ImportAsync(Issues(Board));

        Assert.Equal(2, report.MealsAdded);
        Assert.Equal(0, report.MealsUpdated);

        var meal = await db.Context.Meals.Include(m => m.Tags).SingleAsync(m => m.JiraKey == "FOOD-1");
        Assert.Equal("A meal", meal.Name);
        Assert.Equal(MealStatus.Available, meal.Status);
        Assert.Contains("Simmer it.", meal.Directions);
        Assert.Equal(meal.Directions, meal.ImportedText);
        Assert.True(meal.NeedsIngredientSplit);
        Assert.Empty(meal.Ingredients);
        Assert.Equal(["oven", "quick"], meal.Tags.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task A_one_time_marker_comes_off_the_name_and_onto_the_meal()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);

        await service.ImportAsync(Issues(Board));

        var meal = await db.Context.Meals.SingleAsync(m => m.JiraKey == "FOOD-2");
        Assert.Equal("Another meal", meal.Name);
        Assert.True(meal.IsOneTime);
        Assert.Equal(MealStatus.PlannedShared, meal.Status);
    }

    [Fact]
    public async Task An_empty_description_leaves_nothing_to_split()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);

        await service.ImportAsync(Issues(Board));

        var meal = await db.Context.Meals.SingleAsync(m => m.JiraKey == "FOOD-2");
        Assert.Null(meal.Directions);
        Assert.Null(meal.ImportedText);
        Assert.False(meal.NeedsIngredientSplit);
    }

    [Fact]
    public async Task The_day_a_meal_was_last_cooked_starts_from_when_the_issue_last_changed()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);

        await service.ImportAsync(Issues(Board));

        var meal = await db.Context.Meals.SingleAsync(m => m.JiraKey == "FOOD-1");
        Assert.Equal(new DateOnly(2026, 6, 1), meal.LastCookedAt);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), meal.CreatedAt);
    }

    [Fact]
    public async Task Labels_are_filed_under_the_kind_they_were_given()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);

        await service.ImportAsync(Issues(Board), new Dictionary<string, MealTagFacet>
        {
            ["quick"] = MealTagFacet.Effort,
            ["oven"] = MealTagFacet.Appliance,
        });

        var tags = await new MealTagService(db.Context).ListAsync();
        Assert.Equal(MealTagFacet.Effort, tags.Single(t => t.Name == "quick").Facet);
        Assert.Equal(MealTagFacet.Appliance, tags.Single(t => t.Name == "oven").Facet);
    }

    [Fact]
    public async Task A_label_with_no_kind_given_becomes_a_trait()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);

        var report = await service.ImportAsync(Issues(Board));

        Assert.Equal(2, report.TagsAdded);
        Assert.All(await new MealTagService(db.Context).ListAsync(), tag => Assert.Equal(MealTagFacet.Trait, tag.Facet));
    }

    [Fact]
    public async Task A_tag_already_in_use_keeps_the_kind_it_has()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tags = new MealTagService(db.Context);
        await tags.EnsureAsync(new MealTagRequest("quick", MealTagFacet.Effort));
        var service = new JiraMealImportService(db.Context);

        await service.ImportAsync(Issues(Board), new Dictionary<string, MealTagFacet> { ["quick"] = MealTagFacet.Cuisine });

        Assert.Equal(MealTagFacet.Effort, (await tags.ListAsync()).Single(t => t.Name == "quick").Facet);
    }

    [Fact]
    public async Task Running_it_again_updates_rather_than_duplicates()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);
        await service.ImportAsync(Issues(Board));

        var again = await service.ImportAsync(Issues(Board));

        Assert.Equal(0, again.MealsAdded);
        Assert.Equal(2, again.MealsUpdated);
        Assert.Equal(0, again.TagsAdded);
        Assert.Equal(2, await db.Context.Meals.CountAsync());
    }

    [Fact]
    public async Task A_re_import_leaves_the_day_a_meal_was_cooked_where_the_app_put_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);
        await service.ImportAsync(Issues(Board));

        var meals = new MealService(db.Context, db.Clock);
        var meal = await db.Context.Meals.SingleAsync(m => m.JiraKey == "FOOD-1");
        await meals.UpdateAsync(meal.Id, new UpdateMealRequest(LastCookedAt: new DateOnly(2026, 10, 20)));

        db.Context.ChangeTracker.Clear();
        await service.ImportAsync(Issues(Board));

        db.Context.ChangeTracker.Clear();
        Assert.Equal(new DateOnly(2026, 10, 20), (await db.Context.Meals.SingleAsync(m => m.JiraKey == "FOOD-1")).LastCookedAt);
    }

    [Fact]
    public async Task A_status_with_no_mapping_lands_in_the_pool_and_is_reported()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);

        var report = await service.ImportAsync(Issues("""
        { "issues": [ { "key": "FOOD-9", "fields": { "summary": "A meal", "status": { "name": "Marinating" } } } ] }
        """));

        Assert.Equal(MealStatus.Pooled, (await db.Context.Meals.SingleAsync()).Status);
        Assert.Contains(report.Warnings, warning => warning.Contains("Marinating"));
    }

    [Fact]
    public async Task An_issue_with_no_key_or_no_summary_is_left_out()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new JiraMealImportService(db.Context);

        var report = await service.ImportAsync(Issues("""
        { "issues": [
            { "fields": { "summary": "No key" } },
            { "key": "FOOD-8", "fields": { "summary": "   " } },
            { "key": "FOOD-7", "fields": { "summary": "[One-Time]" } } ] }
        """));

        Assert.Equal(0, report.MealsAdded);
        Assert.Equal(3, report.Skipped);
        Assert.Contains(report.Warnings, warning => warning.Contains("3 issue(s)"));
    }

    [Fact]
    public void The_labels_on_a_board_come_back_once_each_in_order()
    {
        var labels = JiraMealImportService.Labels(Issues("""
        { "issues": [
            { "key": "FOOD-1", "fields": { "summary": "One", "labels": [ "oven", "quick" ] } },
            { "key": "FOOD-2", "fields": { "summary": "Two", "labels": [ "quick", "italian" ] } } ] }
        """));

        Assert.Equal(["italian", "oven", "quick"], labels);
    }
}
