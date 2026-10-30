using CapybaraHome.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="TaskCloseOperationRun"/> to its table.</summary>
public class TaskCloseOperationRunConfiguration : IEntityTypeConfiguration<TaskCloseOperationRun>
{
    /// <summary>Configures the run mapping.</summary>
    public void Configure(EntityTypeBuilder<TaskCloseOperationRun> builder)
    {
        builder.Property(r => r.TaskTitle).IsRequired().HasMaxLength(500);
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(r => r.Status);

        // A run outlives the operation and the task it came from: the record of what happened,
        // a failed call above all, is the point of the row, so neither deletion takes it away.
        builder.HasOne(r => r.Operation)
            .WithMany()
            .HasForeignKey(r => r.OperationId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.Task)
            .WithMany()
            .HasForeignKey(r => r.TaskId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
