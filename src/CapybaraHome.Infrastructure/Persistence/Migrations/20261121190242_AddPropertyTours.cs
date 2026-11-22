using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyTours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TourAiBaseUrl",
                table: "HomeSearchSettings",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TourAiModel",
                table: "HomeSearchSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TourAiTimeoutSeconds",
                table: "HomeSearchSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3600);

            migrationBuilder.AddColumn<string>(
                name: "TourTranscriptionModel",
                table: "HomeSearchSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PropertyAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    StoredName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ByteCount = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyAttachments_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PropertyTours",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PropertyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    PhotoIds = table.Column<string>(type: "TEXT", nullable: false),
                    VideoAttachmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    Transcript = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyTours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PropertyTours_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalTable: "Properties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttachments_PropertyId",
                table: "PropertyAttachments",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyAttachments_StoredName",
                table: "PropertyAttachments",
                column: "StoredName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyTours_PropertyId",
                table: "PropertyTours",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyTours_Status",
                table: "PropertyTours",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PropertyAttachments");

            migrationBuilder.DropTable(
                name: "PropertyTours");

            migrationBuilder.DropColumn(
                name: "TourAiBaseUrl",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "TourAiModel",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "TourAiTimeoutSeconds",
                table: "HomeSearchSettings");

            migrationBuilder.DropColumn(
                name: "TourTranscriptionModel",
                table: "HomeSearchSettings");
        }
    }
}
