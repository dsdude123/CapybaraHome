using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Grocery;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Tasks;
using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Application.Abstractions;

/// <summary>The persistence surface the application services work against.</summary>
public interface ICapybaraHomeDbContext
{
    /// <summary>Task categories.</summary>
    DbSet<Category> Categories { get; }

    /// <summary>Tasks.</summary>
    DbSet<TaskItem> Tasks { get; }

    /// <summary>Meals.</summary>
    DbSet<Meal> Meals { get; }

    /// <summary>Lines of the meals' ingredient lists.</summary>
    DbSet<MealIngredient> MealIngredients { get; }

    /// <summary>Tags meals are filtered by.</summary>
    DbSet<MealTag> MealTags { get; }

    /// <summary>Versions of a meal that change its directions or its ingredients.</summary>
    DbSet<MealVariant> MealVariants { get; }

    /// <summary>Lines the variants add to their meals' ingredient lists.</summary>
    DbSet<MealVariantIngredient> MealVariantIngredients { get; }

    /// <summary>The meals' ingredients the variants leave out.</summary>
    DbSet<MealVariantRemoval> MealVariantRemovals { get; }

    /// <summary>How many meals each slot aims for.</summary>
    DbSet<MealPlanTarget> MealPlanTargets { get; }

    /// <summary>Shopping lists.</summary>
    DbSet<GroceryList> GroceryLists { get; }

    /// <summary>Things to buy on the shopping lists.</summary>
    DbSet<GroceryItem> GroceryItems { get; }

    /// <summary>Items asked about every time a list is generated.</summary>
    DbSet<FrequentGroceryItem> FrequentGroceryItems { get; }

    /// <summary>Dashboard cards.</summary>
    DbSet<DashboardCard> DashboardCards { get; }

    /// <summary>Users allowed to sign in.</summary>
    DbSet<AppUser> Users { get; }

    /// <summary>Bearer tokens issued to the connector.</summary>
    DbSet<ApiToken> ApiTokens { get; }

    /// <summary>Writes the pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
