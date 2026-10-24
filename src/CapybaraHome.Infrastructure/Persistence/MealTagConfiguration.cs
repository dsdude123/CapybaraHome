using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="MealTag"/> to its table.</summary>
public class MealTagConfiguration : IEntityTypeConfiguration<MealTag>
{
    /// <summary>Configures the tag mapping.</summary>
    public void Configure(EntityTypeBuilder<MealTag> builder)
    {
        builder.Property(t => t.Name).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Facet).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(t => t.Name).IsUnique();
    }
}
