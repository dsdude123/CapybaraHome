using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMealVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MealVariants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MealId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DirectionsModifications = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealVariants_Meals_MealId",
                        column: x => x.MealId,
                        principalTable: "Meals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealVariantIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MealVariantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Item = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealVariantIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealVariantIngredients_MealVariants_MealVariantId",
                        column: x => x.MealVariantId,
                        principalTable: "MealVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MealVariantRemovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MealVariantId = table.Column<int>(type: "INTEGER", nullable: false),
                    MealIngredientId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MealVariantRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MealVariantRemovals_MealIngredients_MealIngredientId",
                        column: x => x.MealIngredientId,
                        principalTable: "MealIngredients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MealVariantRemovals_MealVariants_MealVariantId",
                        column: x => x.MealVariantId,
                        principalTable: "MealVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MealVariantIngredients_MealVariantId_SortOrder",
                table: "MealVariantIngredients",
                columns: new[] { "MealVariantId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MealVariantRemovals_MealIngredientId",
                table: "MealVariantRemovals",
                column: "MealIngredientId");

            migrationBuilder.CreateIndex(
                name: "IX_MealVariantRemovals_MealVariantId_MealIngredientId",
                table: "MealVariantRemovals",
                columns: new[] { "MealVariantId", "MealIngredientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MealVariants_MealId_SortOrder",
                table: "MealVariants",
                columns: new[] { "MealId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MealVariantIngredients");

            migrationBuilder.DropTable(
                name: "MealVariantRemovals");

            migrationBuilder.DropTable(
                name: "MealVariants");
        }
    }
}
