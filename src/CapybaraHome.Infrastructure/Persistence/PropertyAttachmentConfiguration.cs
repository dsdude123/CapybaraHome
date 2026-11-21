using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyAttachment"/> to its table.</summary>
public class PropertyAttachmentConfiguration : IEntityTypeConfiguration<PropertyAttachment>
{
    /// <summary>Configures the attachment mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyAttachment> builder)
    {
        builder.Property(a => a.StoredName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.FileName).IsRequired().HasMaxLength(500);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);

        builder.HasIndex(a => a.StoredName).IsUnique();
    }
}
