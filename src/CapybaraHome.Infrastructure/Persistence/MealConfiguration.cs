using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="Meal"/> to its table.</summary>
public class MealConfiguration : IEntityTypeConfiguration<Meal>
{
    /// <summary>Configures the meal mapping.</summary>
    public void Configure(EntityTypeBuilder<Meal> builder)
    {
        builder.Property(m => m.Name).IsRequired().HasMaxLength(500);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.JiraKey).HasMaxLength(50);

        builder.HasIndex(m => m.JiraKey).IsUnique().HasFilter("\"JiraKey\" IS NOT NULL");
        builder.HasIndex(m => m.Status);
        builder.HasIndex(m => m.LastCookedAt);

        builder.HasMany(m => m.Ingredients)
            .WithOne(i => i.Meal)
            .HasForeignKey(i => i.MealId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Variants)
            .WithOne(v => v.Meal)
            .HasForeignKey(v => v.MealId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Tags)
            .WithMany(t => t.Meals)
            .UsingEntity(join => join.ToTable("MealTagLinks"));
    }
}
