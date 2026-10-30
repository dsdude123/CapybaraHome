using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealPlanSharedTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MealPlanSharedTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealPlanSharedTargets", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealPlanSharedTargets");
        }
    }
}
