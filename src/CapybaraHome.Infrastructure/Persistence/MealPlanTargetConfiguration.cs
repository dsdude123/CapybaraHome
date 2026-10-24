using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="MealPlanTarget"/> to its table.</summary>
public class MealPlanTargetConfiguration : IEntityTypeConfiguration<MealPlanTarget>
{
    /// <summary>Configures the target mapping.</summary>
    public void Configure(EntityTypeBuilder<MealPlanTarget> builder)
    {
        builder.HasKey(t => t.Slot);
        builder.Property(t => t.Slot).HasConversion<string>().HasMaxLength(20);
    }
}
