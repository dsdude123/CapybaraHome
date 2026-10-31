using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="SearchPriority"/> to its table.</summary>
public class SearchPriorityConfiguration : IEntityTypeConfiguration<SearchPriority>
{
    /// <summary>Configures the priority mapping.</summary>
    public void Configure(EntityTypeBuilder<SearchPriority> builder)
    {
        builder.Property(p => p.Name).IsRequired().HasMaxLength(300);
        builder.Property(p => p.Points).HasConversion<double>();
    }
}
