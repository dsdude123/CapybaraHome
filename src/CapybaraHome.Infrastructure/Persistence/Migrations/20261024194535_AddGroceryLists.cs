using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroceryLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FrequentGroceryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Quantity = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Item = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false, collation: "NOCASE"),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrequentGroceryItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroceryLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GroceryItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GroceryListId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Item = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroceryItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroceryItems_GroceryLists_GroceryListId",
                        column: x => x.GroceryListId,
                        principalTable: "GroceryLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FrequentGroceryItems_Item",
                table: "FrequentGroceryItems",
                column: "Item",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrequentGroceryItems_SortOrder",
                table: "FrequentGroceryItems",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_GroceryItems_GroceryListId_SortOrder",
                table: "GroceryItems",
                columns: new[] { "GroceryListId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_GroceryLists_ClosedAt",
                table: "GroceryLists",
                column: "ClosedAt");

            // Over the expression rather than the column: SQLite counts NULLs as distinct, so a
            // unique index on ClosedAt itself would let a second open list in.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_GroceryLists_Open\" ON \"GroceryLists\" "
                + "((\"ClosedAt\" IS NULL)) WHERE \"ClosedAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_GroceryLists_Open\";");

            migrationBuilder.DropTable(
                name: "FrequentGroceryItems");

            migrationBuilder.DropTable(
                name: "GroceryItems");

            migrationBuilder.DropTable(
                name: "GroceryLists");
        }
    }
}
