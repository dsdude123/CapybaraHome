using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>EF Core context over the CapybaraHome SQLite database.</summary>
public class CapybaraHomeDbContext(DbContextOptions<CapybaraHomeDbContext> options) : DbContext(options)
{
    /// <summary>Task categories.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Tasks.</summary>
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    /// <summary>Stamps <c>CreatedAt</c> and <c>UpdatedAt</c> on tasks before saving.</summary>
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

    private void StampTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<TaskItem>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
