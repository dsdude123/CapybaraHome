using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyPoint"/> to its table.</summary>
public class PropertyPointConfiguration : IEntityTypeConfiguration<PropertyPoint>
{
    /// <summary>Configures the pro and con mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyPoint> builder)
    {
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Text).IsRequired().HasMaxLength(1000);
    }
}
