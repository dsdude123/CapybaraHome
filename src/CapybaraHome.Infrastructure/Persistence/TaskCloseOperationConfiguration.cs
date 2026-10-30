using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="TaskCloseOperation"/> to its table.</summary>
public class TaskCloseOperationConfiguration : IEntityTypeConfiguration<TaskCloseOperation>
{
    /// <summary>Configures the operation mapping.</summary>
    public void Configure(EntityTypeBuilder<TaskCloseOperation> builder)
    {
        builder.Property(o => o.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.WebhookMethod).HasConversion<string>().HasMaxLength(10);
        builder.Property(o => o.DueDateMode).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.WebhookUrl).HasMaxLength(2000);
        builder.Property(o => o.FollowUpTitle).HasMaxLength(500);

        builder.HasIndex(o => o.TaskId);

        builder.HasOne(o => o.Task)
            .WithMany()
            .HasForeignKey(o => o.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
