using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlannedSlot",
                table: "Meals",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlannedVariantId",
                table: "Meals",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Meals_PlannedVariantId",
                table: "Meals",
                column: "PlannedVariantId");

            migrationBuilder.AddForeignKey(
                name: "FK_Meals_MealVariants_PlannedVariantId",
                table: "Meals",
                column: "PlannedVariantId",
                principalTable: "MealVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Meals_MealVariants_PlannedVariantId",
                table: "Meals");

            migrationBuilder.DropIndex(
                name: "IX_Meals_PlannedVariantId",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "PlannedSlot",
                table: "Meals");

            migrationBuilder.DropColumn(
                name: "PlannedVariantId",
                table: "Meals");
        }
    }
}
