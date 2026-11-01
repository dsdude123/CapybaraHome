using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertySize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SquareFeet",
                table: "Properties",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SizePointsPerSquareFoot",
                table: "HomeSearchSettings",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SquareFeet",
                table: "Properties");

            migrationBuilder.DropColumn(
                name: "SizePointsPerSquareFoot",
                table: "HomeSearchSettings");
        }
    }
}
