using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyTour"/> to its table.</summary>
public class PropertyTourConfiguration : IEntityTypeConfiguration<PropertyTour>
{
    /// <summary>Configures the tour mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyTour> builder)
    {
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Error).HasMaxLength(2000);

        builder.HasIndex(t => t.Status);
    }
}
