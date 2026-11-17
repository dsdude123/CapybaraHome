using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="SearchProfile"/> to its table.</summary>
public class SearchProfileConfiguration : IEntityTypeConfiguration<SearchProfile>
{
    /// <summary>Configures the profile mapping.</summary>
    public void Configure(EntityTypeBuilder<SearchProfile> builder)
    {
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.MonthlyFeePointsPerDollar).HasConversion<double>();
        builder.Property(p => p.DepositPointsPerDollar).HasConversion<double>();
        builder.Property(p => p.DriveTimePointsPerMinute).HasConversion<double>();
        builder.Property(p => p.AmenityPoints).HasConversion<double>();
        builder.Property(p => p.ProPoints).HasConversion<double>();
        builder.Property(p => p.ConPoints).HasConversion<double>();
        builder.Property(p => p.SizePointsPerSquareFoot).HasConversion<double>();

        // Deleting a scenario takes its destinations, cars and tolls with it, and the drive times
        // to those destinations in turn.
        builder.HasMany(p => p.Destinations).WithOne(d => d.Profile!).HasForeignKey(d => d.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.Vehicles).WithOne(v => v.Profile!).HasForeignKey(v => v.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(p => p.TollRoads).WithOne(t => t.Profile!).HasForeignKey(t => t.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
