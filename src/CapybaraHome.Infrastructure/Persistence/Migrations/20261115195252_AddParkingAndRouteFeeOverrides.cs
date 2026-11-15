using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParkingAndRouteFeeOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The credit only changed its name, so the column is renamed rather than replaced,
            // which keeps what each destination already pays back. Parking is new and starts at
            // nothing. EF guessed the other way round, which would have read the saved credits as
            // parking charges.
            migrationBuilder.RenameColumn(
                name: "CarpoolCreditPerDay",
                table: "SearchDestinations",
                newName: "CommuteCreditPerDay");

            migrationBuilder.AddColumn<double>(
                name: "ParkingPerDay",
                table: "SearchDestinations",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "CommuteCreditPerDayOverride",
                table: "PropertyDriveTimes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ParkingPerDayOverride",
                table: "PropertyDriveTimes",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParkingPerDay",
                table: "SearchDestinations");

            migrationBuilder.DropColumn(
                name: "CommuteCreditPerDayOverride",
                table: "PropertyDriveTimes");

            migrationBuilder.DropColumn(
                name: "ParkingPerDayOverride",
                table: "PropertyDriveTimes");

            migrationBuilder.RenameColumn(
                name: "CommuteCreditPerDay",
                table: "SearchDestinations",
                newName: "CarpoolCreditPerDay");
        }
    }
}
