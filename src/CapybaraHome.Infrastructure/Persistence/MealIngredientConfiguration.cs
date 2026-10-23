using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="MealIngredient"/> to its table.</summary>
public class MealIngredientConfiguration : IEntityTypeConfiguration<MealIngredient>
{
    /// <summary>Configures the ingredient mapping.</summary>
    public void Configure(EntityTypeBuilder<MealIngredient> builder)
    {
        builder.Property(i => i.Item).IsRequired().HasMaxLength(300);
        builder.Property(i => i.Quantity).HasMaxLength(100);
        builder.HasIndex(i => new { i.MealId, i.SortOrder });
    }
}
