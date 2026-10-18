using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="Category"/> to its table.</summary>
public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <summary>Configures the category mapping.</summary>
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.ColorHex).HasMaxLength(7);
        builder.HasIndex(c => c.SortOrder);
    }
}
