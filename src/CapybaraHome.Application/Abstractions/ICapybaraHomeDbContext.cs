using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Homes;
using CapybaraHome.Domain.Grocery;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Reports;
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

    /// <summary>Rules that create tasks on a timer.</summary>
    DbSet<TaskAutomation> TaskAutomations { get; }

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

    /// <summary>Homes being tracked, candidates and the one lived in now.</summary>
    DbSet<Property> Properties { get; }

    /// <summary>Lines of the properties' utility breakdowns.</summary>
    DbSet<PropertyUtility> PropertyUtilities { get; }

    /// <summary>What the properties offer.</summary>
    DbSet<PropertyAmenity> PropertyAmenities { get; }

    /// <summary>The written pros and cons.</summary>
    DbSet<PropertyPoint> PropertyPoints { get; }

    /// <summary>Notes written about the properties.</summary>
    DbSet<PropertyNote> PropertyNotes { get; }

    /// <summary>Pictures of the properties.</summary>
    DbSet<PropertyPhoto> PropertyPhotos { get; }

    /// <summary>Places the drive from each property is measured to.</summary>
    DbSet<SearchDestination> SearchDestinations { get; }

    /// <summary>The measured and typed drive times.</summary>
    DbSet<PropertyDriveTime> PropertyDriveTimes { get; }

    /// <summary>What a home has to do, each worth its own points.</summary>
    DbSet<SearchPriority> SearchPriorities { get; }

    /// <summary>How the properties were marked against the priorities.</summary>
    DbSet<PropertyPriority> PropertyPriorities { get; }

    /// <summary>The cars the drives are made in.</summary>
    DbSet<Vehicle> Vehicles { get; }

    /// <summary>The toll roads and what each crossing costs.</summary>
    DbSet<TollRoad> TollRoads { get; }

    /// <summary>The scoring weights and the routing service the search uses.</summary>
    DbSet<HomeSearchSettings> HomeSearchSettings { get; }

    /// <summary>The one row that says how the morning report is built and printed.</summary>
    DbSet<DailyReportSettings> DailyReportSettings { get; }

    /// <summary>Calendar feeds the morning report reads.</summary>
    DbSet<ReportCalendar> ReportCalendars { get; }

    /// <summary>Categories the morning report's task table is kept to.</summary>
    DbSet<ReportCategory> ReportCategories { get; }

    /// <summary>How many of the planned meals are meant to be shared ones.</summary>
    DbSet<MealPlanSharedTarget> MealPlanSharedTargets { get; }

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
