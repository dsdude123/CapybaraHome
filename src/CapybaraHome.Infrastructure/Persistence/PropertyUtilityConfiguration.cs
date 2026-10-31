using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyUtility"/> to its table.</summary>
public class PropertyUtilityConfiguration : IEntityTypeConfiguration<PropertyUtility>
{
    /// <summary>Configures the utility line mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyUtility> builder)
    {
        builder.Property(u => u.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(u => u.Label).HasMaxLength(200);
        builder.Property(u => u.MonthlyCost).HasConversion<double>();
    }
}
