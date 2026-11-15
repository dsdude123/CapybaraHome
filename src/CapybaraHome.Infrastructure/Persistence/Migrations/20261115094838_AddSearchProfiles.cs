using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    MonthlyFeePointsPerDollar = table.Column<double>(type: "REAL", nullable: false),
                    DepositPointsPerDollar = table.Column<double>(type: "REAL", nullable: false),
                    DriveTimePointsPerMinute = table.Column<double>(type: "REAL", nullable: false),
                    AmenityPoints = table.Column<double>(type: "REAL", nullable: false),
                    ProPoints = table.Column<double>(type: "REAL", nullable: false),
                    ConPoints = table.Column<double>(type: "REAL", nullable: false),
                    SizePointsPerSquareFoot = table.Column<double>(type: "REAL", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchProfiles", x => x.Id);
                });

            // The weights already set, and the destinations, cars and tolls already saved, are one
            // scenario: they move into the first profile rather than starting again.
            migrationBuilder.Sql(
                """
                INSERT INTO "SearchProfiles" (
                    "Name", "IsActive", "SortOrder",
                    "MonthlyFeePointsPerDollar", "DepositPointsPerDollar", "DriveTimePointsPerMinute",
                    "AmenityPoints", "ProPoints", "ConPoints", "SizePointsPerSquareFoot", "UpdatedAt")
                SELECT
                    'Default', 1, 0,
                    COALESCE((SELECT "MonthlyFeePointsPerDollar" FROM "HomeSearchSettings" LIMIT 1), 0),
                    COALESCE((SELECT "DepositPointsPerDollar" FROM "HomeSearchSettings" LIMIT 1), 0),
                    COALESCE((SELECT "DriveTimePointsPerMinute" FROM "HomeSearchSettings" LIMIT 1), 0),
                    COALESCE((SELECT "AmenityPoints" FROM "HomeSearchSettings" LIMIT 1), 0),
                    COALESCE((SELECT "ProPoints" FROM "HomeSearchSettings" LIMIT 1), 0),
                    COALESCE((SELECT "ConPoints" FROM "HomeSearchSettings" LIMIT 1), 0),
                    COALESCE((SELECT "SizePointsPerSquareFoot" FROM "HomeSearchSettings" LIMIT 1), 0),
                    COALESCE((SELECT "UpdatedAt" FROM "HomeSearchSettings" LIMIT 1), '0001-01-01 00:00:00+00:00');
                """);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "Vehicles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "TollRoads",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "SearchDestinations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "Vehicles" SET "ProfileId" = (SELECT "Id" FROM "SearchProfiles" ORDER BY "Id" LIMIT 1);
                """);

            migrationBuilder.Sql(
                """
                UPDATE "TollRoads" SET "ProfileId" = (SELECT "Id" FROM "SearchProfiles" ORDER BY "Id" LIMIT 1);
                """);

            migrationBuilder.Sql(
                """
                UPDATE "SearchDestinations" SET "ProfileId" = (SELECT "Id" FROM "SearchProfiles" ORDER BY "Id" LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_ProfileId",
                table: "Vehicles",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TollRoads_ProfileId",
                table: "TollRoads",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchDestinations_ProfileId",
                table: "SearchDestinations",
                column: "ProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_SearchDestinations_SearchProfiles_ProfileId",
                table: "SearchDestinations",
                column: "ProfileId",
                principalTable: "SearchProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TollRoads_SearchProfiles_ProfileId",
                table: "TollRoads",
                column: "ProfileId",
                principalTable: "SearchProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_SearchProfiles_ProfileId",
                table: "Vehicles",
                column: "ProfileId",
                principalTable: "SearchProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropColumn(
                name: "AmenityPoints",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "ConPoints",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "DepositPointsPerDollar",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "DriveTimePointsPerMinute",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "MonthlyFeePointsPerDollar",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "ProPoints",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "SizePointsPerSquareFoot",
                table: "HomeSearchSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SearchDestinations_SearchProfiles_ProfileId",
                table: "SearchDestinations");

            migrationBuilder.DropForeignKey(
                name: "FK_TollRoads_SearchProfiles_ProfileId",
                table: "TollRoads");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_SearchProfiles_ProfileId",
                table: "Vehicles");

            migrationBuilder.DropTable(
                name: "SearchProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_ProfileId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_TollRoads_ProfileId",
                table: "TollRoads");

            migrationBuilder.DropIndex(
                name: "IX_SearchDestinations_ProfileId",
                table: "SearchDestinations");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "TollRoads");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "SearchDestinations");

            migrationBuilder.AddColumn<double>(
                name: "AmenityPoints",
                table: "HomeSearchSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ConPoints",
                table: "HomeSearchSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DepositPointsPerDollar",
                table: "HomeSearchSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "DriveTimePointsPerMinute",
                table: "HomeSearchSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "MonthlyFeePointsPerDollar",
                table: "HomeSearchSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "ProPoints",
                table: "HomeSearchSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "SizePointsPerSquareFoot",
                table: "HomeSearchSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
