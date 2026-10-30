using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="HomeSearchSettings"/> to its single-row table.</summary>
public class HomeSearchSettingsConfiguration : IEntityTypeConfiguration<HomeSearchSettings>
{
    /// <summary>Configures the settings mapping.</summary>
    public void Configure(EntityTypeBuilder<HomeSearchSettings> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.MonthlyFeePointsPerDollar).HasConversion<double>();
        builder.Property(s => s.DepositPointsPerDollar).HasConversion<double>();
        builder.Property(s => s.DriveTimePointsPerMinute).HasConversion<double>();
        builder.Property(s => s.AmenityPoints).HasConversion<double>();
        builder.Property(s => s.ProPoints).HasConversion<double>();
        builder.Property(s => s.ConPoints).HasConversion<double>();
        builder.Property(s => s.RoutingBaseUrl).HasMaxLength(500);
        builder.Property(s => s.RoutingApiKey).HasMaxLength(500);
    }
}
