using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="TaskItem"/> to its table.</summary>
public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    /// <summary>Configures the task mapping.</summary>
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.Property(t => t.Title).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.JiraKey).HasMaxLength(50);

        builder.HasIndex(t => t.JiraKey).IsUnique().HasFilter("\"JiraKey\" IS NOT NULL");
        builder.HasIndex(t => t.DueDate);
        builder.HasIndex(t => t.Status);

        builder.HasOne(t => t.Category)
            .WithMany(c => c.Tasks)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
