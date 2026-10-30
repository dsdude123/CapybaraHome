using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Homes;
using CapybaraHome.Domain.Grocery;
using CapybaraHome.Domain.Meals;
using CapybaraHome.Domain.Reports;
using CapybaraHome.Domain.Tasks;
using CapybaraHome.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>EF Core context over the CapybaraHome SQLite database.</summary>
public class CapybaraHomeDbContext(DbContextOptions<CapybaraHomeDbContext> options)
    : DbContext(options), ICapybaraHomeDbContext
{
    /// <summary>Task categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Tasks.</summary>
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    /// <summary>Rules that create tasks on a timer.</summary>
    public DbSet<TaskAutomation> TaskAutomations => Set<TaskAutomation>();

    /// <summary>Meals.</summary>
    public DbSet<Meal> Meals => Set<Meal>();

    /// <summary>Lines of the meals' ingredient lists.</summary>
    public DbSet<MealIngredient> MealIngredients => Set<MealIngredient>();

    /// <summary>Tags meals are filtered by.</summary>
    public DbSet<MealTag> MealTags => Set<MealTag>();

    /// <summary>Versions of a meal that change its directions or its ingredients.</summary>
    public DbSet<MealVariant> MealVariants => Set<MealVariant>();

    /// <summary>How many meals each slot aims for.</summary>
    public DbSet<MealPlanTarget> MealPlanTargets => Set<MealPlanTarget>();

    /// <summary>How many of the planned meals are meant to be shared ones.</summary>
    public DbSet<MealPlanSharedTarget> MealPlanSharedTargets => Set<MealPlanSharedTarget>();

    /// <summary>Lines the variants add to their meals' ingredient lists.</summary>
    public DbSet<MealVariantIngredient> MealVariantIngredients => Set<MealVariantIngredient>();

    /// <summary>The meals' ingredients the variants leave out.</summary>
    public DbSet<MealVariantRemoval> MealVariantRemovals => Set<MealVariantRemoval>();

    /// <summary>Homes being tracked, candidates and the one lived in now.</summary>
    public DbSet<Property> Properties => Set<Property>();

    /// <summary>Lines of the properties' utility breakdowns.</summary>
    public DbSet<PropertyUtility> PropertyUtilities => Set<PropertyUtility>();

    /// <summary>What the properties offer.</summary>
    public DbSet<PropertyAmenity> PropertyAmenities => Set<PropertyAmenity>();

    /// <summary>The written pros and cons.</summary>
    public DbSet<PropertyPoint> PropertyPoints => Set<PropertyPoint>();

    /// <summary>Notes written about the properties.</summary>
    public DbSet<PropertyNote> PropertyNotes => Set<PropertyNote>();

    /// <summary>Pictures of the properties.</summary>
    public DbSet<PropertyPhoto> PropertyPhotos => Set<PropertyPhoto>();

    /// <summary>Places the drive from each property is measured to.</summary>
    public DbSet<SearchDestination> SearchDestinations => Set<SearchDestination>();

    /// <summary>The measured and typed drive times.</summary>
    public DbSet<PropertyDriveTime> PropertyDriveTimes => Set<PropertyDriveTime>();

    /// <summary>What a home has to do, each worth its own points.</summary>
    public DbSet<SearchPriority> SearchPriorities => Set<SearchPriority>();

    /// <summary>How the properties were marked against the priorities.</summary>
    public DbSet<PropertyPriority> PropertyPriorities => Set<PropertyPriority>();

    /// <summary>The cars the drives are made in.</summary>
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    /// <summary>The toll roads and what each crossing costs.</summary>
    public DbSet<TollRoad> TollRoads => Set<TollRoad>();

    /// <summary>The scoring weights and the routing service the search uses.</summary>
    public DbSet<HomeSearchSettings> HomeSearchSettings => Set<HomeSearchSettings>();

    /// <summary>The one row that says how the morning report is built and printed.</summary>
    public DbSet<DailyReportSettings> DailyReportSettings => Set<DailyReportSettings>();

    /// <summary>Calendar feeds the morning report reads.</summary>
    public DbSet<ReportCalendar> ReportCalendars => Set<ReportCalendar>();

    /// <summary>Categories the morning report's task table is kept to.</summary>
    public DbSet<ReportCategory> ReportCategories => Set<ReportCategory>();

    /// <summary>Shopping lists.</summary>
    public DbSet<GroceryList> GroceryLists => Set<GroceryList>();

    /// <summary>Things to buy on the shopping lists.</summary>
    public DbSet<GroceryItem> GroceryItems => Set<GroceryItem>();

    /// <summary>Items asked about every time a list is generated.</summary>
    public DbSet<FrequentGroceryItem> FrequentGroceryItems => Set<FrequentGroceryItem>();

    /// <summary>Cards on the shared dashboard.</summary>
    public DbSet<DashboardCard> DashboardCards => Set<DashboardCard>();

    /// <summary>Users allowed to sign in.</summary>
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <summary>Bearer tokens issued to the connector.</summary>
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    /// <summary>Stamps <c>CreatedAt</c> and <c>UpdatedAt</c> on tasks, meals and properties before saving.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc cref="SaveChangesAsync(CancellationToken)"/>
    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    /// <summary>Applies the entity configuration in this assembly.</summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CapybaraHomeDbContext).Assembly);
    }

    // An import carries the JIRA timestamps across, so a value the caller set explicitly is
    // left alone and only the untouched ones are stamped.
    private void StampTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<TaskItem>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                if (entry.Entity.UpdatedAt == default)
                {
                    entry.Entity.UpdatedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified && !entry.Property(t => t.UpdatedAt).IsModified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Property>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified && !entry.Property(p => p.UpdatedAt).IsModified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Meal>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                if (entry.Entity.UpdatedAt == default)
                {
                    entry.Entity.UpdatedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified && !entry.Property(m => m.UpdatedAt).IsModified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
