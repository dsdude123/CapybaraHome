using CapybaraHome.Domain.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CapybaraHome.Infrastructure.Persistence;

/// <summary>Maps <see cref="DailyReportSettings"/> to its table.</summary>
public class DailyReportSettingsConfiguration : IEntityTypeConfiguration<DailyReportSettings>
{
    /// <summary>Configures the settings mapping.</summary>
    public void Configure(EntityTypeBuilder<DailyReportSettings> builder)
    {
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.PrinterHost).HasMaxLength(500);
        builder.Property(s => s.PrinterQueue).IsRequired().HasMaxLength(500);
        builder.Property(s => s.WeatherUrl).HasMaxLength(2000);
        builder.Property(s => s.LastRunError).HasMaxLength(2000);
    }
}

/// <summary>Maps <see cref="ReportCalendar"/> to its table.</summary>
public class ReportCalendarConfiguration : IEntityTypeConfiguration<ReportCalendar>
{
    /// <summary>Configures the calendar mapping.</summary>
    public void Configure(EntityTypeBuilder<ReportCalendar> builder)
    {
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Url).IsRequired().HasMaxLength(2000);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => new { c.Kind, c.SortOrder });
    }
}

/// <summary>Maps <see cref="ReportCategory"/> to its table.</summary>
public class ReportCategoryConfiguration : IEntityTypeConfiguration<ReportCategory>
{
    /// <summary>Configures the tracked-category mapping.</summary>
    public void Configure(EntityTypeBuilder<ReportCategory> builder)
    {
        builder.HasKey(c => c.CategoryId);

        // A category that is deleted stops being tracked, rather than leaving a row that keeps
        // the whole report to a category nobody can see any more.
        builder.HasOne(c => c.Category)
            .WithMany()
            .HasForeignKey(c => c.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
