using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyDriveTime"/> to its table.</summary>
public class PropertyDriveTimeConfiguration : IEntityTypeConfiguration<PropertyDriveTime>
{
    /// <summary>Configures the drive time mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyDriveTime> builder)
    {
        builder.Property(d => d.Source).HasConversion<string>().HasMaxLength(10);

        // One time per property and destination, so a fresh measurement replaces the old one.
        builder.HasIndex(d => new { d.PropertyId, d.DestinationId }).IsUnique();

        builder.HasOne(d => d.Destination)
            .WithMany(destination => destination.DriveTimes)
            .HasForeignKey(d => d.DestinationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
