using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>Covers the variants a meal can carry, which share the meal's status and dates.</summary>
public class MealVariantTests
{
    [Fact]
    public async Task A_variant_adds_and_removes_ingredients_of_its_meal()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal", Ingredients:
            [new MealIngredientInput("first"), new MealIngredientInput("second")]));
        var dropped = meal.Ingredients.Single(i => i.Item == "second");

        var variant = await service.CreateVariantAsync(meal.Id, new MealVariantRequest(
            "  A variant  ", "Leave it out", [new MealIngredientInput("extra", "1 tin")], [dropped.Id]));

        Assert.Equal("A variant", variant!.Name);
        Assert.Equal("extra", Assert.Single(variant.AddedIngredients).Item);
        Assert.Equal(dropped.Id, Assert.Single(variant.RemovedIngredientIds));
        Assert.Equal(meal.Id, variant.MealId);
    }

    [Fact]
    public async Task A_blank_variant_name_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateVariantAsync(meal.Id, new MealVariantRequest("  ")));
    }

    [Fact]
    public async Task A_variant_cannot_leave_out_an_ingredient_of_another_meal()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var other = await service.CreateAsync(new CreateMealRequest("Another meal", Ingredients: [new MealIngredientInput("theirs")]));
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateVariantAsync(
            meal.Id, new MealVariantRequest("A variant", RemovedIngredientIds: [other.Ingredients[0].Id])));
    }

    [Fact]
    public async Task Adding_a_variant_to_a_meal_that_is_gone_returns_nothing()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);

        Assert.Null(await service.CreateVariantAsync(404, new MealVariantRequest("A variant")));
    }

    [Fact]
    public async Task A_variant_shows_up_as_a_change_to_its_meal()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));

        db.Clock.Advance(TimeSpan.FromHours(1));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("A variant"));

        var after = await service.GetAsync(meal.Id);
        Assert.Equal(db.Clock.GetUtcNow(), after!.UpdatedAt);
        Assert.Equal(meal.LastCookedAt, after.LastCookedAt);
        Assert.Equal(meal.Status, after.Status);
    }

    [Fact]
    public async Task Updating_a_variant_replaces_what_it_adds_and_removes()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal", Ingredients:
            [new MealIngredientInput("first"), new MealIngredientInput("second")]));
        var variant = await service.CreateVariantAsync(meal.Id, new MealVariantRequest(
            "A variant", null, [new MealIngredientInput("extra")], [meal.Ingredients[0].Id]));

        var updated = await service.UpdateVariantAsync(variant!.Id, new MealVariantRequest(
            "A renamed variant", "now with notes", [new MealIngredientInput("another")], [meal.Ingredients[1].Id]));

        Assert.Equal("A renamed variant", updated!.Name);
        Assert.Equal("now with notes", updated.DirectionsModifications);
        Assert.Equal("another", Assert.Single(updated.AddedIngredients).Item);
        Assert.Equal(meal.Ingredients[1].Id, Assert.Single(updated.RemovedIngredientIds));
        Assert.Single(db.Context.MealVariantIngredients);
        Assert.Single(db.Context.MealVariantRemovals);
    }

    [Fact]
    public async Task Dropping_an_ingredient_from_the_meal_drops_the_variants_pointing_at_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal", Ingredients: [new MealIngredientInput("first")]));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("A variant", RemovedIngredientIds: [meal.Ingredients[0].Id]));

        await service.UpdateAsync(meal.Id, new UpdateMealRequest(Ingredients: [new MealIngredientInput("something else")]));

        db.Context.ChangeTracker.Clear();
        var after = await service.GetAsync(meal.Id);
        Assert.Empty(Assert.Single(after!.Variants).RemovedIngredientIds);
        Assert.Empty(db.Context.MealVariantRemovals);
    }

    [Fact]
    public async Task Deleting_a_meal_takes_its_variants_with_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal", Ingredients: [new MealIngredientInput("first")]));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest(
            "A variant", null, [new MealIngredientInput("extra")], [meal.Ingredients[0].Id]));

        Assert.True(await service.DeleteAsync(meal.Id));

        Assert.Empty(db.Context.MealVariants);
        Assert.Empty(db.Context.MealVariantIngredients);
        Assert.Empty(db.Context.MealVariantRemovals);
    }

    [Fact]
    public async Task A_variant_can_be_deleted_on_its_own()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));
        var variant = await service.CreateVariantAsync(meal.Id, new MealVariantRequest("A variant"));

        Assert.True(await service.DeleteVariantAsync(variant!.Id));
        Assert.False(await service.DeleteVariantAsync(variant.Id));

        db.Context.ChangeTracker.Clear();
        Assert.Empty((await service.GetAsync(meal.Id))!.Variants);
        Assert.NotNull(await db.Context.Meals.SingleAsync());
    }

    [Fact]
    public async Task Variants_keep_the_order_they_were_added_in()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealService(db.Context, db.Clock);
        var meal = await service.CreateAsync(new CreateMealRequest("A meal"));

        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("First"));
        await service.CreateVariantAsync(meal.Id, new MealVariantRequest("Second"));

        var after = await service.GetAsync(meal.Id);
        Assert.Equal(["First", "Second"], after!.Variants.Select(v => v.Name));
    }
}
