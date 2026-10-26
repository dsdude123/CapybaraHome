using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CapybaraHome.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCloseOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskCloseOperations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    WebhookUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    WebhookMethod = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    FollowUpTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DueDateMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DueDateValue = table.Column<int>(type: "INTEGER", nullable: true),
                    DueDateOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CarryOperationsForward = table.Column<bool>(type: "INTEGER", nullable: false),
                    DocumentTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DocumentBody = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCloseOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskCloseOperations_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskCloseOperationRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperationId = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Detail = table.Column<string>(type: "TEXT", nullable: true),
                    WebhookPayload = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedTaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    DocumentTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DocumentBody = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCloseOperationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskCloseOperationRuns_TaskCloseOperations_OperationId",
                        column: x => x.OperationId,
                        principalTable: "TaskCloseOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TaskCloseOperationRuns_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskCloseOperationRuns_OperationId",
                table: "TaskCloseOperationRuns",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCloseOperationRuns_Status",
                table: "TaskCloseOperationRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCloseOperationRuns_TaskId",
                table: "TaskCloseOperationRuns",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCloseOperations_TaskId",
                table: "TaskCloseOperations",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskCloseOperationRuns");

            migrationBuilder.DropTable(
                name: "TaskCloseOperations");
        }
    }
}
