using CapybaraHome.Domain.Meals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="MealPlanSharedTarget"/> to its table.</summary>
public class MealPlanSharedTargetConfiguration : IEntityTypeConfiguration<MealPlanSharedTarget>
{
    /// <summary>Configures the shared target mapping.</summary>
    public void Configure(EntityTypeBuilder<MealPlanSharedTarget> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
    }
}
