using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDriveTimeMultiplierAndTollOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DriveTimeMultiplier",
                table: "SearchDestinations",
                type: "REAL",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<string>(
                name: "TollRoadOverrideIds",
                table: "PropertyDriveTimes",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriveTimeMultiplier",
                table: "SearchDestinations");

            migrationBuilder.DropColumn(
                name: "TollRoadOverrideIds",
                table: "PropertyDriveTimes");
        }
    }
}
