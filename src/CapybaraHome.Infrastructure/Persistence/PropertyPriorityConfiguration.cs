using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyPriority"/> to its join table.</summary>
public class PropertyPriorityConfiguration : IEntityTypeConfiguration<PropertyPriority>
{
    /// <summary>Configures the marking mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyPriority> builder)
    {
        builder.HasKey(m => new { m.PropertyId, m.PriorityId });

        builder.HasOne(m => m.Priority)
            .WithMany(p => p.Properties)
            .HasForeignKey(m => m.PriorityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
