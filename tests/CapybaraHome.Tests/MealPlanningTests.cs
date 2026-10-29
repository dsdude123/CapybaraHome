using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Meals;

namespace CapybaraHome.Tests;

/// <summary>Covers putting meals and variants on the plan and taking them off again.</summary>
public class MealPlanningTests
{
    [Fact]
    public async Task Planning_a_meal_fills_a_slot_and_leaves_the_cooked_date_alone()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var cookedOn = new DateOnly(2026, 9, 1);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal", LastCookedAt: cookedOn));

        var planned = await service.PlanAsync(meal.Id, MealSlot.Lunch);

        Assert.Equal(MealStatus.Planned, planned!.Status);
        Assert.Equal(MealSlot.Lunch, planned.PlannedSlot);
        Assert.Null(planned.PlannedVariantId);
        Assert.Equal(cookedOn, planned.LastCookedAt);
    }

    [Fact]
    public async Task A_shared_meal_is_planned_as_shared()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));

        var planned = await service.PlanAsync(meal.Id, MealSlot.Dinner, shared: true);

        Assert.Equal(MealStatus.PlannedShared, planned!.Status);
    }

    [Fact]
    public async Task A_variant_can_be_planned_in_place_of_its_meal()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));
        var variant = await service.CreateVariantAsync(meal.Id, new MealVariantRequest("A variant"));

        var planned = await service.PlanAsync(meal.Id, MealSlot.Dinner, variant!.Id);

        Assert.Equal(variant.Id, planned!.PlannedVariantId);
        Assert.Equal("A variant", planned.PlannedVariantName);
    }

    [Fact]
    public async Task A_variant_of_another_meal_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var other = await service.CreateAsync(new CreateMealRequest("Another meal"));
        var theirs = await service.CreateVariantAsync(other.Id, new MealVariantRequest("Their variant"));
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.PlanAsync(meal.Id, MealSlot.Dinner, theirs!.Id));
    }

    [Fact]
    public async Task Planning_a_meal_that_is_gone_returns_nothing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        Assert.Null(await service.PlanAsync(404, MealSlot.Dinner));
    }

    [Fact]
    public async Task Sending_a_planned_meal_back_clears_the_slot_and_the_variant()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));
        var variant = await service.CreateVariantAsync(meal.Id, new MealVariantRequest("A variant"));
        await service.PlanAsync(meal.Id, MealSlot.Dinner, variant!.Id);

        var back = await service.UpdateAsync(meal.Id, new UpdateMealRequest(Status: MealStatus.Available));

        Assert.Null(back!.PlannedSlot);
        Assert.Null(back.PlannedVariantId);
        Assert.Equal(DateOnly.FromDateTime(db.Clock.GetUtcNow().UtcDateTime), back.LastCookedAt);
    }

    [Fact]
    public async Task Moving_a_planned_meal_between_slots_keeps_it_planned()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));
        await service.PlanAsync(meal.Id, MealSlot.Dinner);

        var moved = await service.PlanAsync(meal.Id, MealSlot.Breakfast, shared: true);

        Assert.Equal(MealSlot.Breakfast, moved!.PlannedSlot);
        Assert.Equal(MealStatus.PlannedShared, moved.Status);
        Assert.Null(moved.LastCookedAt);
    }

    [Fact]
    public async Task Deleting_the_planned_variant_leaves_the_meal_planned_as_itself()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));
        var variant = await service.CreateVariantAsync(meal.Id, new MealVariantRequest("A variant"));
        await service.PlanAsync(meal.Id, MealSlot.Dinner, variant!.Id);

        await service.DeleteVariantAsync(variant.Id);

        db.Context.ChangeTracker.Clear();
        var after = await service.GetAsync(meal.Id);
        Assert.Equal(MealStatus.Planned, after!.Status);
        Assert.Null(after.PlannedVariantId);
    }

    [Fact]
    public async Task The_options_list_offers_the_meal_once_however_many_variants_it_has()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var cookedOn = new DateOnly(2026, 9, 1);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal", LastCookedAt: cookedOn, Status: MealStatus.Queued));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("First"));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("Second"));

        var options = await service.ListOptionsAsync(new MealFilter());

        var option = Assert.Single(options);
        Assert.Equal("A meal", option.Name);
        Assert.True(option.HasVariants);
        Assert.Equal(cookedOn, option.LastCookedAt);
        Assert.Equal(MealStatus.Queued, option.Status);
    }

    [Fact]
    public async Task The_limit_counts_meals()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("First"));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("Second"));
        await service.CreateAsync(new CreateMealRequest("Another meal"));
        await service.CreateAsync(new CreateMealRequest("A third meal"));

        var options = await service.ListOptionsAsync(new MealFilter(Limit: 2), MealSort.Name);

        Assert.Equal(["A meal", "A third meal"], options.Select(o => o.Name));
    }

    [Fact]
    public async Task A_meal_with_no_variants_is_offered_with_nothing_to_choose()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        await service.CreateAsync(new CreateMealRequest("A meal"));

        var option = Assert.Single(await service.ListOptionsAsync(new MealFilter()));

        Assert.False(option.HasVariants);
    }

    [Theory]
    [InlineData("A variant")]
    [InlineData("chopped")]
    [InlineData("twice as long")]
    public async Task A_search_that_matches_a_variant_returns_its_meal(string text)
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest(
            "A variant",
            "Cook it twice as long.",
            [new MealIngredientInput("chopped nuts", "1 cup")]));

        var option = Assert.Single(await service.ListOptionsAsync(new MealFilter(Text: text)));

        Assert.Equal("A meal", option.Name);
        Assert.Equal(meal.Id, option.MealId);
    }

    [Fact]
    public async Task A_search_that_matches_nothing_in_a_meal_or_its_variants_returns_nothing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("A variant"));

        Assert.Empty(await service.ListOptionsAsync(new MealFilter(Text: "nothing like it")));
    }
}
