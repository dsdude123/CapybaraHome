using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="PropertyNote"/> to its table.</summary>
public class PropertyNoteConfiguration : IEntityTypeConfiguration<PropertyNote>
{
    /// <summary>Configures the note mapping.</summary>
    public void Configure(EntityTypeBuilder<PropertyNote> builder)
    {
        builder.Property(n => n.Body).IsRequired();
    }
}
