using CapybaraHome.Application.Abstractions;
using CapybaraHome.Domain.Dashboards;
using CapybaraHome.Domain.Meals;
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

    /// <summary>Lines the variants add to their meals' ingredient lists.</summary>
    public DbSet<MealVariantIngredient> MealVariantIngredients => Set<MealVariantIngredient>();

    /// <summary>The meals' ingredients the variants leave out.</summary>
    public DbSet<MealVariantRemoval> MealVariantRemovals => Set<MealVariantRemoval>();

    /// <summary>Cards on the shared dashboard.</summary>
    public DbSet<DashboardCard> DashboardCards => Set<DashboardCard>();

    /// <summary>Users allowed to sign in.</summary>
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <summary>Bearer tokens issued to the connector.</summary>
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    /// <summary>Stamps <c>CreatedAt</c> and <c>UpdatedAt</c> on tasks and meals before saving.</summary>
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
