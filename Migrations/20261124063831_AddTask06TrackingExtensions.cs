using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConstructionProjectManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddTask06TrackingExtensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskDependencies_TaskId",
                table: "TaskDependencies");

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Tasks",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "TaskProgressHistories",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "TaskDependencies",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedById",
                table: "TaskDependencies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DependencyType",
                table: "TaskDependencies",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "FS");

            migrationBuilder.CreateTable(
                name: "TaskAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    OldValue = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PerformedById = table.Column<string>(type: "TEXT", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAuditLogs_AspNetUsers_PerformedById",
                        column: x => x.PerformedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAuditLogs_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorId = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsBlocker = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProgressSnapshot = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskComments_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskComments_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_CompletionPercentage",
                table: "Tasks",
                sql: "CAST(\"PercentComplete\" AS NUMERIC) >= 0 AND CAST(\"PercentComplete\" AS NUMERIC) <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Tasks_PlannedDates",
                table: "Tasks",
                sql: "\"PlannedEndDate\" >= \"PlannedStartDate\"");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_CreatedById",
                table: "TaskDependencies",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_TaskId_PredecessorTaskId",
                table: "TaskDependencies",
                columns: new[] { "TaskId", "PredecessorTaskId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TaskDependencies_NotSelf",
                table: "TaskDependencies",
                sql: "\"TaskId\" <> \"PredecessorTaskId\"");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAuditLogs_PerformedById",
                table: "TaskAuditLogs",
                column: "PerformedById");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAuditLogs_TaskId",
                table: "TaskAuditLogs",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_AuthorId",
                table: "TaskComments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_TaskId",
                table: "TaskComments",
                column: "TaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskDependencies_AspNetUsers_CreatedById",
                table: "TaskDependencies",
                column: "CreatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskDependencies_AspNetUsers_CreatedById",
                table: "TaskDependencies");

            migrationBuilder.DropTable(
                name: "TaskAuditLogs");

            migrationBuilder.DropTable(
                name: "TaskComments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_CompletionPercentage",
                table: "Tasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Tasks_PlannedDates",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_TaskDependencies_CreatedById",
                table: "TaskDependencies");

            migrationBuilder.DropIndex(
                name: "IX_TaskDependencies_TaskId_PredecessorTaskId",
                table: "TaskDependencies");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TaskDependencies_NotSelf",
                table: "TaskDependencies");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "TaskProgressHistories");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "TaskDependencies");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "TaskDependencies");

            migrationBuilder.DropColumn(
                name: "DependencyType",
                table: "TaskDependencies");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_TaskId",
                table: "TaskDependencies",
                column: "TaskId");
        }
    }
}
