using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="TaskAutomation"/> to its table.</summary>
public class TaskAutomationConfiguration : IEntityTypeConfiguration<TaskAutomation>
{
    /// <summary>Configures the automation mapping.</summary>
    public void Configure(EntityTypeBuilder<TaskAutomation> builder)
    {
        builder.Property(a => a.Title).IsRequired().HasMaxLength(500);
        builder.Property(a => a.IntervalUnit).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(a => a.IsEnabled);

        builder.HasOne(a => a.Category)
            .WithMany()
            .HasForeignKey(a => a.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
