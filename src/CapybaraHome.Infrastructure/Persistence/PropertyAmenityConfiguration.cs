using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyAmenity"/> to its table.</summary>
public class PropertyAmenityConfiguration : IEntityTypeConfiguration<PropertyAmenity>
{
    /// <summary>Configures the amenity mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyAmenity> builder)
    {
        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
    }
}
