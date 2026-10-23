using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Meals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Directions = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsOneTime = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCookedAt = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ImportedText = table.Column<string>(type: "TEXT", nullable: true),
                    NeedsIngredientSplit = table.Column<bool>(type: "INTEGER", nullable: false),
                    JiraKey = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Facet = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MealIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MealId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Item = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealIngredients_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealTagLinks",
                columns: table => new
                {
                    MealsId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagsId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealTagLinks", x => new { x.MealsId, x.TagsId });
                    table.ForeignKey(
                        name: "FK_MealTagLinks_MealTags_TagsId",
                        column: x => x.TagsId,
                        principalTable: "MealTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MealTagLinks_Meals_MealsId",
                        column: x => x.MealsId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealIngredients_MealId_SortOrder",
                table: "MealIngredients",
                columns: new[] { "MealId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Meals_JiraKey",
                table: "Meals",
                column: "JiraKey",
                unique: true,
                filter: "\"JiraKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_LastCookedAt",
                table: "Meals",
                column: "LastCookedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Meals_Status",
                table: "Meals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_MealTagLinks_TagsId",
                table: "MealTagLinks",
                column: "TagsId");

            migrationBuilder.CreateIndex(
                name: "IX_MealTags_Name",
                table: "MealTags",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealIngredients");

            migrationBuilder.DropTable(
                name: "MealTagLinks");

            migrationBuilder.DropTable(
                name: "MealTags");

            migrationBuilder.DropTable(
                name: "Meals");
        }
    }
}
