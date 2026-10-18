using CapybaraHome.Domain.Dashboards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="DashboardCard"/> to its table.</summary>
public class DashboardCardConfiguration : IEntityTypeConfiguration<DashboardCard>
{
    /// <summary>Configures the dashboard card mapping.</summary>
    public void Configure(EntityTypeBuilder<DashboardCard> builder)
    {
        builder.ToTable("DashboardCards");
        builder.Property(c => c.Title).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(c => c.SortOrder);

        builder.HasOne(c => c.Category)
            .WithMany()
            .HasForeignKey(c => c.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
