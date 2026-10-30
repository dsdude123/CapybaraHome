using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyPhoto"/> to its table.</summary>
public class PropertyPhotoConfiguration : IEntityTypeConfiguration<PropertyPhoto>
{
    /// <summary>Configures the picture mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyPhoto> builder)
    {
        builder.Property(p => p.StoredName).IsRequired().HasMaxLength(100);
        builder.Property(p => p.FileName).IsRequired().HasMaxLength(500);
        builder.Property(p => p.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Caption).HasMaxLength(500);

        builder.HasIndex(p => p.StoredName).IsUnique();
    }
}
