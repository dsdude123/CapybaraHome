using CapybaraHome.Application.Meals;
using CapybaraHome.Domain.Meals;

namespace CapybaraHome.Tests;

/// <summary>Covers the tags the meal filters are built from.</summary>
public class MealTagServiceTests
{
    [Fact]
    public async Task Ensuring_a_tag_twice_returns_the_one_tag()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealTagService(db.Context);

        var first = await service.EnsureAsync(new MealTagRequest("  oven  ", MealTagFacet.Appliance));
        var again = await service.EnsureAsync(new MealTagRequest("oven", MealTagFacet.Cuisine));

        Assert.Equal("oven", first.Name);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(MealTagFacet.Appliance, again.Facet);
        Assert.Single(await service.ListAsync());
    }

    [Fact]
    public async Task A_tag_typed_in_another_case_is_the_same_tag()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealTagService(db.Context);

        var first = await service.EnsureAsync(new MealTagRequest("quick", MealTagFacet.Effort));
        var again = await service.EnsureAsync(new MealTagRequest("Quick", MealTagFacet.Effort));

        Assert.Equal(first.Id, again.Id);
        Assert.Single(await service.ListAsync());
    }

    [Fact]
    public async Task A_blank_name_is_refused()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealTagService(db.Context);

        await Assert.ThrowsAsync<ArgumentException>(() => service.EnsureAsync(new MealTagRequest("   ")));
    }

    [Fact]
    public async Task A_tag_can_be_renamed_and_moved_to_another_facet()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealTagService(db.Context);
        var tag = await service.EnsureAsync(new MealTagRequest("quik", MealTagFacet.Trait));

        var updated = await service.UpdateAsync(tag.Id, new MealTagRequest("quick", MealTagFacet.Effort));

        Assert.Equal("quick", updated!.Name);
        Assert.Equal(MealTagFacet.Effort, updated.Facet);
    }

    [Fact]
    public async Task Deleting_a_tag_takes_it_off_the_meals_carrying_it()
    {
        await using var db = await TestDatabase.CreateAsync();
        var tags = new MealTagService(db.Context);
        var meals = new MealService(db.Context, db.Clock);
        var tag = await tags.EnsureAsync(new MealTagRequest("quick", MealTagFacet.Effort));
        var meal = await meals.CreateAsync(new CreateMealRequest("A meal", TagIds: [tag.Id]));

        Assert.True(await tags.DeleteAsync(tag.Id));

        db.Context.ChangeTracker.Clear();
        Assert.Empty((await meals.GetAsync(meal.Id))!.Tags);
    }

    [Fact]
    public async Task Tags_come_back_grouped_by_facet()
    {
        await using var db = await TestDatabase.CreateAsync();
        var service = new MealTagService(db.Context);
        await service.EnsureAsync(new MealTagRequest("leftovers", MealTagFacet.Trait));
        await service.EnsureAsync(new MealTagRequest("italian", MealTagFacet.Cuisine));
        await service.EnsureAsync(new MealTagRequest("quick", MealTagFacet.Effort));

        var listed = await service.ListAsync();

        Assert.Equal(["italian", "quick", "leftovers"], listed.Select(t => t.Name));
    }
}
