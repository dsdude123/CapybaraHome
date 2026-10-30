using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHomeSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HomeSearchSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    MonthlyFeePointsPerDollar = table.Column<double>(type: "REAL", nullable: false),
                    DepositPointsPerDollar = table.Column<double>(type: "REAL", nullable: false),
                    DriveTimePointsPerMinute = table.Column<double>(type: "REAL", nullable: false),
                    AmenityPoints = table.Column<double>(type: "REAL", nullable: false),
                    ProPoints = table.Column<double>(type: "REAL", nullable: false),
                    ConPoints = table.Column<double>(type: "REAL", nullable: false),
                    RoutingBaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RoutingApiKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeSearchSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Properties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    Rent = table.Column<double>(type: "REAL", nullable: true),
                    ParkingFee = table.Column<double>(type: "REAL", nullable: true),
                    PetRent = table.Column<double>(type: "REAL", nullable: true),
                    Deposit = table.Column<double>(type: "REAL", nullable: true),
                    Internet = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Properties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchDestinations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Weight = table.Column<double>(type: "REAL", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchDestinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchPriorities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Points = table.Column<double>(type: "REAL", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchPriorities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PropertyAmenities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyAmenities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyAmenities_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyNotes_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    StoredName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ByteCount = table.Column<long>(type: "INTEGER", nullable: false),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyPhotos_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyPoints_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyUtilities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MonthlyCost = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyUtilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyUtilities_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyDriveTimes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    DestinationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Minutes = table.Column<double>(type: "REAL", nullable: false),
                    Kilometres = table.Column<double>(type: "REAL", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    MeasuredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyDriveTimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyDriveTimes_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PropertyDriveTimes_SearchDestinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "SearchDestinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyPriorities",
                columns: table => new
                {
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    PriorityId = table.Column<int>(type: "INTEGER", nullable: false),
                    Meets = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyPriorities", x => new { x.PropertyId, x.PriorityId });
                    table.ForeignKey(
                        name: "FK_PropertyPriorities_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PropertyPriorities_SearchPriorities_PriorityId",
                        column: x => x.PriorityId,
                        principalTable: "SearchPriorities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_IsCurrent",
                table: "Properties",
                column: "IsCurrent");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAmenities_PropertyId",
                table: "PropertyAmenities",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyDriveTimes_DestinationId",
                table: "PropertyDriveTimes",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyDriveTimes_PropertyId_DestinationId",
                table: "PropertyDriveTimes",
                columns: new[] { "PropertyId", "DestinationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyNotes_PropertyId",
                table: "PropertyNotes",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyPhotos_PropertyId",
                table: "PropertyPhotos",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyPhotos_StoredName",
                table: "PropertyPhotos",
                column: "StoredName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyPoints_PropertyId",
                table: "PropertyPoints",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyPriorities_PriorityId",
                table: "PropertyPriorities",
                column: "PriorityId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyUtilities_PropertyId",
                table: "PropertyUtilities",
                column: "PropertyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomeSearchSettings");

            migrationBuilder.DropTable(
                name: "PropertyAmenities");

            migrationBuilder.DropTable(
                name: "PropertyDriveTimes");

            migrationBuilder.DropTable(
                name: "PropertyNotes");

            migrationBuilder.DropTable(
                name: "PropertyPhotos");

            migrationBuilder.DropTable(
                name: "PropertyPoints");

            migrationBuilder.DropTable(
                name: "PropertyPriorities");

            migrationBuilder.DropTable(
                name: "PropertyUtilities");

            migrationBuilder.DropTable(
                name: "SearchDestinations");

            migrationBuilder.DropTable(
                name: "SearchPriorities");

            migrationBuilder.DropTable(
                name: "Properties");
        }
    }
}
