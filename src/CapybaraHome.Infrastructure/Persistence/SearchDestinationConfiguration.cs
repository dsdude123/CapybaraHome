using CapybaraHome.Domain.Homes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="SearchDestination"/> to its table.</summary>
public class SearchDestinationConfiguration : IEntityTypeConfiguration<SearchDestination>
{
    /// <summary>Configures the destination mapping.</summary>
    public void Configure(EntityTypeBuilder<SearchDestination> builder)
    {
        builder.Property(d => d.Name).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Address).HasMaxLength(1000);
        builder.Property(d => d.Weight).HasConversion<double>();
    }
}
