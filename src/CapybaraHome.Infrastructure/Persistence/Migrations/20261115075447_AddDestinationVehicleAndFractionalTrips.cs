using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDestinationVehicleAndFractionalTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "RoundTripsPerWeek",
                table: "SearchDestinations",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<int>(
                name: "VehicleId",
                table: "SearchDestinations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchDestinations_VehicleId",
                table: "SearchDestinations",
                column: "VehicleId");

            migrationBuilder.AddForeignKey(
                name: "FK_SearchDestinations_Vehicles_VehicleId",
                table: "SearchDestinations",
                column: "VehicleId",
                principalTable: "Vehicles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SearchDestinations_Vehicles_VehicleId",
                table: "SearchDestinations");

            migrationBuilder.DropIndex(
                name: "IX_SearchDestinations_VehicleId",
                table: "SearchDestinations");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "SearchDestinations");

            migrationBuilder.AlterColumn<int>(
                name: "RoundTripsPerWeek",
                table: "SearchDestinations",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");
        }
    }
}
