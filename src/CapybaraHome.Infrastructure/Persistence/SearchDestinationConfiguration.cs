using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="SearchDestination"/> to its table.</summary>
public class SearchDestinationConfiguration : IEntityTypeConfiguration<SearchDestination>
{
    /// <summary>Configures the destination mapping.</summary>
    public void Configure(EntityTypeBuilder<SearchDestination> builder)
    {
        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Address).HasMaxLength(1000);
        builder.Property(d => d.Weight).HasConversion<double>();
        builder.Property(d => d.ParkingPerDay).HasConversion<double>();
        builder.Property(d => d.CommuteCreditPerDay).HasConversion<double>();
        builder.Property(d => d.RoundTripsPerWeek).HasConversion<double>();

        // Losing the car a drive is made in leaves the drive shared out rather than deleting it.
        builder.HasOne(d => d.Vehicle)
            .WithMany()
            .HasForeignKey(d => d.VehicleId)
            .OnDelete(DeleteBehavior.SetNull);

        // One, not zero: a destination that existed before the column did multiplies its drives by
        // one, which leaves them as measured.
        builder.Property(d => d.DriveTimeMultiplier).HasDefaultValue(1d);
    }
}
