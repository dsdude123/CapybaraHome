using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="Vehicle"/> to its table.</summary>
public class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    /// <summary>Configures the vehicle mapping.</summary>
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.Property(v => v.Name).IsRequired().HasMaxLength(200);
        builder.Property(v => v.FuelPricePerGallon).HasConversion<double>();
    }
}
