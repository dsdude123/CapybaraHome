using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Meals;

namespace CapybaraHome.Tests;

/// <summary>Covers the filters, the orders and the last cooked rule meals are read and written through.</summary>
public class MealServiceTests
{
    [Fact]
    public async Task Creating_a_meal_trims_the_name_and_starts_it_pooled()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        var created = await service.CreateAsync(new CreateMealRequest("  A meal  "));

        Assert.Equal("A meal", created.Name);
        Assert.Equal(MealStatus.Pooled, created.Status);
        Assert.Null(created.LastCookedAt);
    }

    [Fact]
    public async Task A_blank_name_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateMealRequest("   ")));
    }

    [Fact]
    public async Task Ingredients_keep_their_order_and_blank_lines_are_dropped()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        var created = await service.CreateAsync(new CreateMealRequest("A meal", Ingredients:
        [
            new MealIngredientInput("Second item", "2 cups"),
            new MealIngredientInput("   "),
            new MealIngredientInput("First item", null),
        ]));

        Assert.Equal(["Second item", "First item"], created.Ingredients.Select(i => i.Item));
        Assert.Equal("2 cups", created.Ingredients[0].Quantity);
        Assert.Null(created.Ingredients[1].Quantity);
        Assert.Equal([0, 1], created.Ingredients.Select(i => i.SortOrder));
    }

    [Theory]
    [InlineData(MealStatus.Available)]
    [InlineData(MealStatus.Pooled)]
    public async Task Sending_a_meal_back_stamps_the_day_it_was_cooked(MealStatus back)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var created = await service.CreateAsync(new CreateMealRequest("A meal", Status: MealStatus.Planned));

        var cooked = await service.UpdateAsync(created.Id, new UpdateMealRequest(Status: back));

        Assert.Equal(DateOnly.FromDateTime(db.Clock.GetUtcNow().UtcDateTime), cooked!.LastCookedAt);
    }

    [Fact]
    public async Task Planning_a_meal_leaves_the_day_it_was_cooked_alone()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var cookedOn = new DateOnly(2026, 9, 1);
        var created = await service.CreateAsync(new CreateMealRequest("A meal", LastCookedAt: cookedOn));

        var planned = await service.UpdateAsync(created.Id, new UpdateMealRequest(Status: MealStatus.Planned));

        Assert.Equal(cookedOn, planned!.LastCookedAt);
    }

    [Fact]
    public async Task A_date_given_with_the_status_wins_over_the_stamp()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var created = await service.CreateAsync(new CreateMealRequest("A meal", Status: MealStatus.Planned));
        var chosen = new DateOnly(2026, 8, 15);

        var updated = await service.UpdateAsync(created.Id, new UpdateMealRequest(Status: MealStatus.Available, LastCookedAt: chosen));

        Assert.Equal(chosen, updated!.LastCookedAt);
    }

    [Fact]
    public async Task The_day_it_was_cooked_can_be_cleared()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var created = await service.CreateAsync(new CreateMealRequest("A meal", LastCookedAt: new DateOnly(2026, 9, 1)));

        var cleared = await service.UpdateAsync(created.Id, new UpdateMealRequest(ClearLastCookedAt: true));

        Assert.Null(cleared!.LastCookedAt);
    }

    [Theory]
    [InlineData(MealSort.LastCooked, new[] { "Never", "Long ago", "Recently" })]
    [InlineData(MealSort.LastCookedDescending, new[] { "Recently", "Long ago", "Never" })]
    [InlineData(MealSort.Name, new[] { "Long ago", "Never", "Recently" })]
    [InlineData(MealSort.NameDescending, new[] { "Recently", "Never", "Long ago" })]
    public async Task Every_order_the_picker_offers_reads_against_the_database(MealSort sort, string[] expected)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        await service.CreateAsync(new CreateMealRequest("Recently", LastCookedAt: new DateOnly(2026, 10, 1)));
        await service.CreateAsync(new CreateMealRequest("Never"));
        await service.CreateAsync(new CreateMealRequest("Long ago", LastCookedAt: new DateOnly(2026, 1, 1)));

        var listed = await service.ListAsync(new MealFilter(), sort);

        Assert.Equal(expected, listed.Select(m => m.Name));
    }

    [Fact]
    public async Task The_text_filter_reaches_the_directions_and_the_ingredients()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        await service.CreateAsync(new CreateMealRequest("By name", Directions: "nothing to see"));
        await service.CreateAsync(new CreateMealRequest("By directions", Directions: "simmer the needle"));
        await service.CreateAsync(new CreateMealRequest("By ingredient", Ingredients: [new MealIngredientInput("a needle", "1")]));

        var listed = await service.ListAsync(new MealFilter(Text: "needle"), MealSort.Name);

        Assert.Equal(["By directions", "By ingredient"], listed.Select(m => m.Name));
    }

    [Fact]
    public async Task A_meal_has_to_carry_every_tag_the_filter_names()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var tags = new MealTagService(db.Context);
        var quick = await tags.EnsureAsync(new MealTagRequest("quick", MealTagFacet.Effort));
        var oven = await tags.EnsureAsync(new MealTagRequest("oven", MealTagFacet.Appliance));

        await service.CreateAsync(new CreateMealRequest("Both", TagIds: [quick.Id, oven.Id]));
        await service.CreateAsync(new CreateMealRequest("One", TagIds: [quick.Id]));

        var listed = await service.ListAsync(new MealFilter(TagIds: [quick.Id, oven.Id]));

        Assert.Equal(["Both"], listed.Select(m => m.Name));
    }

    [Fact]
    public async Task The_status_filter_keeps_only_the_states_it_names()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        await service.CreateAsync(new CreateMealRequest("Queued one", Status: MealStatus.Queued));
        await service.CreateAsync(new CreateMealRequest("Closed one", Status: MealStatus.Closed));
        await service.CreateAsync(new CreateMealRequest("Shared one", Status: MealStatus.PlannedShared));

        var listed = await service.ListAsync(new MealFilter(Statuses: [MealStatus.Queued, MealStatus.PlannedShared]), MealSort.Name);

        Assert.Equal(["Queued one", "Shared one"], listed.Select(m => m.Name));
    }

    [Fact]
    public async Task The_split_filter_separates_the_meals_still_to_be_split()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var created = await service.CreateAsync(new CreateMealRequest("Imported"));
        await service.CreateAsync(new CreateMealRequest("Typed in"));
        await service.UpdateAsync(created.Id, new UpdateMealRequest(ImportedText: "a wall of text", NeedsIngredientSplit: true));

        var waiting = await service.ListAsync(new MealFilter(NeedsIngredientSplit: true));
        var done = await service.ListAsync(new MealFilter(NeedsIngredientSplit: false));

        Assert.Equal(["Imported"], waiting.Select(m => m.Name));
        Assert.Equal(["Typed in"], done.Select(m => m.Name));
    }

    [Fact]
    public async Task Updating_the_ingredients_replaces_the_whole_list()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var created = await service.CreateAsync(new CreateMealRequest("A meal", Ingredients:
            [new MealIngredientInput("one"), new MealIngredientInput("two")]));

        var updated = await service.UpdateAsync(created.Id, new UpdateMealRequest(Ingredients: [new MealIngredientInput("three", "1 tin")]));

        Assert.Equal(["three"], updated!.Ingredients.Select(i => i.Item));
        Assert.Single(db.Context.MealIngredients);
    }

    [Fact]
    public async Task Updating_the_tags_replaces_the_whole_set()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var tags = new MealTagService(db.Context);
        var first = await tags.EnsureAsync(new MealTagRequest("first"));
        var second = await tags.EnsureAsync(new MealTagRequest("second"));
        var created = await service.CreateAsync(new CreateMealRequest("A meal", TagIds: [first.Id]));

        var updated = await service.UpdateAsync(created.Id, new UpdateMealRequest(TagIds: [second.Id]));

        Assert.Equal(["second"], updated!.Tags.Select(t => t.Name));
    }

    [Fact]
    public async Task A_tag_that_does_not_exist_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateMealRequest("A meal", TagIds: [404])));
    }

    [Fact]
    public async Task Deleting_a_meal_takes_its_ingredients_with_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var created = await service.CreateAsync(new CreateMealRequest("A meal", Ingredients: [new MealIngredientInput("one")]));

        Assert.True(await service.DeleteAsync(created.Id));
        Assert.Empty(db.Context.Meals);
        Assert.Empty(db.Context.MealIngredients);
    }

    [Fact]
    public async Task A_listing_can_be_paged_and_counted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        foreach (var index in Enumerable.Range(1, 5))
        {
            await service.CreateAsync(new CreateMealRequest($"Meal {index}"));
        }

        var second = await service.ListAsync(new MealFilter(Skip: 2, Limit: 2), MealSort.Name);

        Assert.Equal(5, await service.CountAsync(new MealFilter()));
        Assert.Equal(["Meal 3", "Meal 4"], second.Select(m => m.Name));
    }
}
