using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="Property"/> and the rows hanging off it to their tables.</summary>
public class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    /// <summary>Configures the property mapping.</summary>
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.Property(p => p.Name).IsRequired().HasMaxLength(500);
        builder.Property(p => p.Address).HasMaxLength(1000);
        builder.Property(p => p.Internet).HasMaxLength(500);

        // SQLite keeps a decimal as text, which cannot be compared or ordered in a query, so the
        // money columns are stored as numbers. Scoring adds them up in memory either way.
        builder.Property(p => p.Rent).HasConversion<double?>();
        builder.Property(p => p.ParkingFee).HasConversion<double?>();
        builder.Property(p => p.PetRent).HasConversion<double?>();
        builder.Property(p => p.Deposit).HasConversion<double?>();

        builder.HasIndex(p => p.IsCurrent);

        builder.HasMany(p => p.Utilities)
            .WithOne(u => u.Property)
            .HasForeignKey(u => u.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Amenities)
            .WithOne(a => a.Property)
            .HasForeignKey(a => a.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Points)
            .WithOne(point => point.Property)
            .HasForeignKey(point => point.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Notes)
            .WithOne(n => n.Property)
            .HasForeignKey(n => n.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Photos)
            .WithOne(photo => photo.Property)
            .HasForeignKey(photo => photo.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.DriveTimes)
            .WithOne(d => d.Property)
            .HasForeignKey(d => d.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Priorities)
            .WithOne(m => m.Property)
            .HasForeignKey(m => m.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
