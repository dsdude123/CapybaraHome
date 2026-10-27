using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomationCustomInterval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Frequency",
                table: "TaskAutomations",
                newName: "IntervalUnit");

            migrationBuilder.AddColumn<int>(
                name: "IntervalCount",
                table: "TaskAutomations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            // The six fixed frequencies become the interval each of them stood for, so a database
            // written before this keeps the schedules it had rather than falling back to a default.
            migrationBuilder.Sql("""
                UPDATE "TaskAutomations" SET "IntervalCount" =
                    CASE "IntervalUnit" WHEN 'BiWeekly' THEN 2 WHEN 'Quarterly' THEN 3 ELSE 1 END,
                "IntervalUnit" =
                    CASE "IntervalUnit"
                        WHEN 'Daily' THEN 'Days'
                        WHEN 'Weekly' THEN 'Weeks'
                        WHEN 'BiWeekly' THEN 'Weeks'
                        WHEN 'Monthly' THEN 'Months'
                        WHEN 'Quarterly' THEN 'Months'
                        WHEN 'Yearly' THEN 'Years'
                        ELSE 'Weeks'
                    END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the intervals the old column could name survive going back; anything else
            // becomes the weekly default, because there was no way to write it before.
            migrationBuilder.Sql("""
                UPDATE "TaskAutomations" SET "IntervalUnit" =
                    CASE
                        WHEN "IntervalUnit" = 'Days' AND "IntervalCount" = 1 THEN 'Daily'
                        WHEN "IntervalUnit" = 'Weeks' AND "IntervalCount" = 1 THEN 'Weekly'
                        WHEN "IntervalUnit" = 'Weeks' AND "IntervalCount" = 2 THEN 'BiWeekly'
                        WHEN "IntervalUnit" = 'Months' AND "IntervalCount" = 1 THEN 'Monthly'
                        WHEN "IntervalUnit" = 'Months' AND "IntervalCount" = 3 THEN 'Quarterly'
                        WHEN "IntervalUnit" = 'Years' AND "IntervalCount" = 1 THEN 'Yearly'
                        ELSE 'Weekly'
                    END;
                """);

            migrationBuilder.DropColumn(
                name: "IntervalCount",
                table: "TaskAutomations");

            migrationBuilder.RenameColumn(
                name: "IntervalUnit",
                table: "TaskAutomations",
                newName: "Frequency");
        }
    }
}
