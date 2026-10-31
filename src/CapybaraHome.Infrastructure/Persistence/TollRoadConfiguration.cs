using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="TollRoad"/> to its table.</summary>
public class TollRoadConfiguration : IEntityTypeConfiguration<TollRoad>
{
    /// <summary>Configures the toll road mapping.</summary>
    public void Configure(EntityTypeBuilder<TollRoad> builder)
    {
        builder.Property(t => t.Road).IsRequired().HasMaxLength(200);
        builder.Property(t => t.PeakToll).HasConversion<double>();
    }
}
