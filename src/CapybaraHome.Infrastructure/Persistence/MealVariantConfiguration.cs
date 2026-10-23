using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="MealVariant"/> and the rows hanging off it to their tables.</summary>
public class MealVariantConfiguration : IEntityTypeConfiguration<MealVariant>
{
    /// <summary>Configures the variant mapping.</summary>
    public void Configure(EntityTypeBuilder<MealVariant> builder)
    {
        builder.Property(v => v.Name).IsRequired().HasMaxLength(500);
        builder.HasIndex(v => new { v.MealId, v.SortOrder });

        builder.HasMany(v => v.AddedIngredients)
            .WithOne(i => i.Variant)
            .HasForeignKey(i => i.MealVariantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(v => v.RemovedIngredients)
            .WithOne(r => r.Variant)
            .HasForeignKey(r => r.MealVariantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="MealVariantIngredient"/> to its table.</summary>
public class MealVariantIngredientConfiguration : IEntityTypeConfiguration<MealVariantIngredient>
{
    /// <summary>Configures the added ingredient mapping.</summary>
    public void Configure(EntityTypeBuilder<MealVariantIngredient> builder)
    {
        builder.Property(i => i.Item).IsRequired().HasMaxLength(300);
        builder.Property(i => i.Quantity).HasMaxLength(100);
        builder.HasIndex(i => new { i.MealVariantId, i.SortOrder });
    }
}

/// <summary>Maps <see cref="MealVariantRemoval"/> to its table.</summary>
public class MealVariantRemovalConfiguration : IEntityTypeConfiguration<MealVariantRemoval>
{
    /// <summary>Configures the removal mapping.</summary>
    public void Configure(EntityTypeBuilder<MealVariantRemoval> builder)
    {
        builder.HasIndex(r => new { r.MealVariantId, r.MealIngredientId }).IsUnique();

        // A line dropped from the meal's list takes its removals with it, so a variant can never
        // point at an ingredient that is no longer there.
        builder.HasOne(r => r.Ingredient)
            .WithMany()
            .HasForeignKey(r => r.MealIngredientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
