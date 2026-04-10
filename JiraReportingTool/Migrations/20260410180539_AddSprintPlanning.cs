using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SprintPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SprintStart = table.Column<DateOnly>(type: "date", nullable: false),
                    SprintEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    LoadedEpicKeys = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SprintPlanAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintPlanId = table.Column<int>(type: "int", nullable: false),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    TaskLabel = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EpicKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    HoursAllocated = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintPlanAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintPlanAllocations_SprintPlans_SprintPlanId",
                        column: x => x.SprintPlanId,
                        principalTable: "SprintPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SprintPlanCustomTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintPlanId = table.Column<int>(type: "int", nullable: false),
                    LocalId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Hours = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintPlanCustomTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintPlanCustomTasks_SprintPlans_SprintPlanId",
                        column: x => x.SprintPlanId,
                        principalTable: "SprintPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SprintPlanHolidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintPlanId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintPlanHolidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintPlanHolidays_SprintPlans_SprintPlanId",
                        column: x => x.SprintPlanId,
                        principalTable: "SprintPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SprintPlanTimeOffs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintPlanId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintPlanTimeOffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintPlanTimeOffs_SprintPlans_SprintPlanId",
                        column: x => x.SprintPlanId,
                        principalTable: "SprintPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SprintPlanAllocations_SprintPlanId",
                table: "SprintPlanAllocations",
                column: "SprintPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintPlanCustomTasks_SprintPlanId",
                table: "SprintPlanCustomTasks",
                column: "SprintPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintPlanHolidays_SprintPlanId",
                table: "SprintPlanHolidays",
                column: "SprintPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintPlanTimeOffs_SprintPlanId",
                table: "SprintPlanTimeOffs",
                column: "SprintPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SprintPlanAllocations");

            migrationBuilder.DropTable(
                name: "SprintPlanCustomTasks");

            migrationBuilder.DropTable(
                name: "SprintPlanHolidays");

            migrationBuilder.DropTable(
                name: "SprintPlanTimeOffs");

            migrationBuilder.DropTable(
                name: "SprintPlans");
        }
    }
}
