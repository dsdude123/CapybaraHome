using CapybaraHome.Application.DataTransfer;
using CapybaraHome.Application.Grocery;
using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Grocery;
using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Tests;

/// <summary>Covers the shopping lists, the frequent items and what the generator offers.</summary>
public class GroceryServiceTests
{
    [Fact]
    public async Task There_is_no_list_until_something_needs_one()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);

        Assert.Null(await service.GetCurrentAsync());

        var opened = await service.EnsureCurrentAsync();

        Assert.Empty(opened.Items);
        Assert.Null(opened.ClosedAt);
        Assert.Equal(opened.Id, (await service.GetCurrentAsync())!.Id);
    }

    [Fact]
    public async Task An_item_added_mid_week_goes_on_the_list_that_is_already_open()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        var list = await service.EnsureCurrentAsync();

        await service.AddItemAsync(new GroceryItemInput("milk", "2 pints"));
        db.Clock.Advance(TimeSpan.FromDays(3));
        await service.AddItemAsync(new GroceryItemInput("batteries"));

        var current = (await service.GetCurrentAsync())!;

        Assert.Equal(list.Id, current.Id);
        Assert.Equal(["milk", "batteries"], current.Items.Select(i => i.Item));
        Assert.Equal("2 pints", current.Items[0].Quantity);
    }

    [Fact]
    public async Task A_blank_item_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddItemAsync(new GroceryItemInput("  ")));
    }

    [Fact]
    public async Task An_item_is_taken_off_the_list()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        var item = await service.AddItemAsync(new GroceryItemInput("milk"));

        Assert.True(await service.RemoveItemAsync(item.Id));
        Assert.False(await service.RemoveItemAsync(item.Id));
        Assert.Empty((await service.GetCurrentAsync())!.Items);
    }

    [Fact]
    public async Task A_fresh_list_is_only_started_by_asking_and_keeps_the_one_it_replaces()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        var first = await service.EnsureCurrentAsync();
        await service.AddItemAsync(new GroceryItemInput("milk"));

        db.Clock.Advance(TimeSpan.FromDays(7));
        var second = await service.StartFreshAsync();

        Assert.NotEqual(first.Id, second.Id);
        Assert.Empty(second.Items);
        Assert.Equal(second.Id, (await service.GetCurrentAsync())!.Id);

        var closed = await service.GetAsync(first.Id);

        Assert.NotNull(closed!.ClosedAt);
        Assert.Equal("milk", Assert.Single(closed.Items).Item);

        Assert.Single(await service.ListAsync());
        Assert.Equal(2, (await service.ListAsync(includeClosed: true)).Count);
    }

    [Fact]
    public async Task Adding_the_chosen_items_passes_over_what_the_list_already_holds()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        await service.AddItemAsync(new GroceryItemInput("Milk"));

        var added = await service.AddItemsAsync(
        [
            new GroceryItemInput("milk", "2 pints"),
            new GroceryItemInput("rice", "2 cups", "A meal"),
            new GroceryItemInput("  "),
            new GroceryItemInput("rice"),
        ]);

        var items = (await service.GetCurrentAsync())!.Items;

        Assert.Equal(1, added);
        Assert.Equal(["Milk", "rice"], items.Select(i => i.Item));
        Assert.Equal("A meal", items[1].Source);
    }

    [Fact]
    public async Task The_database_refuses_a_second_open_list_whoever_writes_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        await service.EnsureCurrentAsync();

        // Past the service, straight at the table: the index is what holds the one-open-list rule.
        db.Context.GroceryLists.Add(new GroceryList { StartedAt = db.Clock.GetUtcNow() });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());

        db.Context.ChangeTracker.Clear();
        await service.StartFreshAsync();

        Assert.Single(await service.ListAsync());
        Assert.Equal(2, (await service.ListAsync(includeClosed: true)).Count);
    }

    [Fact]
    public async Task The_database_refuses_a_frequent_item_already_held_in_another_case()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        await service.AddFrequentAsync(new GroceryItemInput("Milk"));

        db.Context.FrequentGroceryItems.Add(new FrequentGroceryItem { Item = "milk", SortOrder = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_frequent_item_is_held_once_whatever_its_case()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);

        var first = await service.AddFrequentAsync(new GroceryItemInput("Milk", "2 pints"));
        var again = await service.AddFrequentAsync(new GroceryItemInput("milk"));

        Assert.Equal(first.Id, again.Id);
        Assert.Equal("2 pints", again.Quantity);
        Assert.Single(await service.ListFrequentAsync());

        Assert.True(await service.RemoveFrequentAsync(first.Id));
        Assert.Empty(await service.ListFrequentAsync());
    }

    [Fact]
    public async Task The_generator_asks_about_the_frequent_items_and_each_planned_meal()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        var meals = new MealService(db.Context, db.Clock);

        await service.AddFrequentAsync(new GroceryItemInput("milk", "2 pints"));

        var planned = await meals.CreateAsync(new CreateMealRequest("Curry", Ingredients:
            [new MealIngredientInput("rice", "2 cups"), new MealIngredientInput("tomatoes", "1 tin")]));
        await meals.PlanAsync(planned.Id, MealSlot.Dinner);

        var pooled = await meals.CreateAsync(new CreateMealRequest("Something unplanned", Ingredients:
            [new MealIngredientInput("beans")]));

        var candidates = await service.CandidatesAsync();

        Assert.Equal([GroceryService.FrequentGroupName, "Curry"], candidates.Groups.Select(g => g.Name));
        Assert.Equal("milk", Assert.Single(candidates.Groups[0].Candidates).Item);
        Assert.Equal(["rice", "tomatoes"], candidates.Groups[1].Candidates.Select(c => c.Item));
        Assert.Equal("Curry", candidates.Groups[1].Candidates[0].Source);
        Assert.Equal("2 cups", candidates.Groups[1].Candidates[0].Quantity);
        Assert.Equal(3, candidates.Offered);

        // Nothing of an unplanned meal is offered; the plan is what decides the list.
        Assert.DoesNotContain("beans", candidates.Groups.SelectMany(g => g.Candidates).Select(c => c.Item));
        Assert.DoesNotContain(pooled.Name, candidates.Groups.Select(g => g.Name));
    }

    [Fact]
    public async Task The_generator_follows_the_planned_variant_rather_than_the_meal()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        var meals = new MealService(db.Context, db.Clock);

        var meal = await meals.CreateAsync(new CreateMealRequest("Curry", Ingredients:
            [new MealIngredientInput("rice", "2 cups"), new MealIngredientInput("chicken")]));

        var variant = await meals.CreateVariantAsync(meal.Id, new MealVariantRequest(
            "Vegetarian",
            AddedIngredients: [new MealIngredientInput("chickpeas", "1 tin")],
            RemovedIngredientIds: [meal.Ingredients.Single(i => i.Item == "chicken").Id]));

        await meals.PlanAsync(meal.Id, MealSlot.Dinner, variant!.Id);

        var group = Assert.Single(await Groups(service));

        Assert.Equal("Curry (Vegetarian)", group.Name);
        Assert.Equal(["rice", "chickpeas"], group.Candidates.Select(c => c.Item));
        Assert.Equal("Curry (Vegetarian)", group.Candidates[1].Source);
    }

    [Fact]
    public async Task What_is_on_the_list_is_marked_rather_than_offered_again()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        var meals = new MealService(db.Context, db.Clock);

        await service.AddFrequentAsync(new GroceryItemInput("milk"));
        var meal = await meals.CreateAsync(new CreateMealRequest("Curry", Ingredients: [new MealIngredientInput("rice")]));
        await meals.PlanAsync(meal.Id, MealSlot.Dinner);

        await service.AddItemAsync(new GroceryItemInput("MILK"));

        var candidates = await service.CandidatesAsync();

        Assert.True(candidates.Groups[0].Candidates[0].AlreadyOnList);
        Assert.False(candidates.Groups[1].Candidates[0].AlreadyOnList);
        Assert.Equal(1, candidates.Offered);
    }

    [Fact]
    public async Task The_meals_are_asked_about_in_the_order_of_the_day()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        var meals = new MealService(db.Context, db.Clock);

        foreach (var (name, slot) in new (string, MealSlot?)[]
        {
            ("Soup", MealSlot.Lunch),
            ("Pancakes", MealSlot.Breakfast),
            ("Curry", MealSlot.Dinner),
            ("Leftovers", null),
        })
        {
            var meal = await meals.CreateAsync(new CreateMealRequest(name, Ingredients: [new MealIngredientInput(name.ToLowerInvariant())]));

            if (slot is { } wanted)
            {
                await meals.PlanAsync(meal.Id, wanted);
            }
            else
            {
                await meals.UpdateAsync(meal.Id, new UpdateMealRequest(Status: MealStatus.Planned));
            }
        }

        var groups = await Groups(service);

        Assert.Equal(["Pancakes", "Soup", "Curry", "Leftovers"], groups.Select(g => g.Name));
    }

    [Fact]
    public async Task A_meal_planned_with_no_ingredients_is_left_out_of_the_prompts()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new GroceryService(db.Context, db.Clock);
        var meals = new MealService(db.Context, db.Clock);
        var meal = await meals.CreateAsync(new CreateMealRequest("Takeaway"));
        await meals.PlanAsync(meal.Id, MealSlot.Dinner);

        Assert.Empty((await service.CandidatesAsync()).Groups);
    }

    [Fact]
    public async Task An_export_carries_the_lists_and_the_frequent_items_back()
    {
        await using var source = await TestDatabase.CreateAsync();
        var service = new GroceryService(source.Context, source.Clock);
        await service.AddFrequentAsync(new GroceryItemInput("milk", "2 pints"));
        await service.AddItemAsync(new GroceryItemInput("rice", "2 cups", "Curry"));
        await service.StartFreshAsync();
        await service.AddItemAsync(new GroceryItemInput("bread"));

        var json = await new DataTransferService(source.Context, source.Clock).ExportToJsonAsync();

        await using var target = await TestDatabase.CreateAsync();
        var summary = await new DataTransferService(target.Context, target.Clock).ImportFromJsonAsync(json, ImportMode.Merge);
        var restored = new GroceryService(target.Context, target.Clock);

        Assert.Equal(2, summary.GroceryListsAdded);
        Assert.Equal(1, summary.FrequentItemsAdded);

        var current = (await restored.GetCurrentAsync())!;
        Assert.Equal("bread", Assert.Single(current.Items).Item);

        var lists = await restored.ListAsync(includeClosed: true);
        var closed = lists.Single(l => l.ClosedAt is not null);
        var carried = Assert.Single(closed.Items);
        Assert.Equal("rice", carried.Item);
        Assert.Equal("Curry", carried.Source);

        var frequent = Assert.Single(await restored.ListFrequentAsync());
        Assert.Equal("milk", frequent.Item);
        Assert.Equal("2 pints", frequent.Quantity);
    }

    [Fact]
    public async Task A_merge_into_a_house_that_is_already_shopping_leaves_its_list_alone()
    {
        await using var source = await TestDatabase.CreateAsync();
        await new GroceryService(source.Context, source.Clock).AddItemAsync(new GroceryItemInput("rice"));
        var json = await new DataTransferService(source.Context, source.Clock).ExportToJsonAsync();

        await using var target = await TestDatabase.CreateAsync();
        var held = new GroceryService(target.Context, target.Clock);
        await held.AddItemAsync(new GroceryItemInput("bread"));

        var summary = await new DataTransferService(target.Context, target.Clock).ImportFromJsonAsync(json, ImportMode.Merge);

        Assert.Equal(0, summary.GroceryListsAdded);
        Assert.Single(await held.ListAsync(includeClosed: true));
        Assert.Equal("bread", Assert.Single((await held.GetCurrentAsync())!.Items).Item);
    }

    [Fact]
    public async Task An_import_carrying_two_open_lists_leaves_only_the_newest_open()
    {
        await using var db = await TestDatabase.CreateAsync();
        var started = db.Clock.GetUtcNow();

        var document = new BackupDocument(
            BackupDocument.CurrentFormatVersion,
            started,
            [],
            [],
            [],
            GroceryLists:
            [
                new GroceryListRecord(started, null, [new GroceryItemRecord(null, "rice", null, 0)]),
                new GroceryListRecord(started.AddDays(1), null, [new GroceryItemRecord(null, "bread", null, 0)]),
            ]);

        var summary = await new DataTransferService(db.Context, db.Clock).ImportAsync(document, ImportMode.Merge);
        var service = new GroceryService(db.Context, db.Clock);

        Assert.Equal(2, summary.GroceryListsAdded);
        Assert.Equal("bread", Assert.Single((await service.GetCurrentAsync())!.Items).Item);
        Assert.Equal(2, (await service.ListAsync(includeClosed: true)).Count);
    }

    private static async Task<IReadOnlyList<GroceryCandidateGroup>> Groups(GroceryService service) =>
        (await service.CandidatesAsync()).Groups;
}
