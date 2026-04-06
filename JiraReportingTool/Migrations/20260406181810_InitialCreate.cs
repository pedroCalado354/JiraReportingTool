using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EpicSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpicSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JiraEpicReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusCategoryKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Assignee = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JiraEpicReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JiraFilters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Jql = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JiraFilters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SprintReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JiraSprintId = table.Column<int>(type: "int", nullable: true),
                    ReportIdentifier = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ProjectKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SprintName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JiraIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JiraEpicReportId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssueType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusCategoryKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Assignee = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalEstimate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalEstimateSeconds = table.Column<int>(type: "int", nullable: false),
                    TimeSpent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "int", nullable: false),
                    RemainingEstimate = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JiraIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JiraIssues_JiraEpicReports_JiraEpicReportId",
                        column: x => x.JiraEpicReportId,
                        principalTable: "JiraEpicReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SprintIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SprintReportId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssueType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusCategoryKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Assignee = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoryPoints = table.Column<int>(type: "int", nullable: true),
                    OriginalEstimate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalEstimateSeconds = table.Column<int>(type: "int", nullable: false),
                    TimeSpent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "int", nullable: false),
                    RemainingEstimate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RemainingEstimateSeconds = table.Column<int>(type: "int", nullable: false),
                    EpicKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EpicName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Labels = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Created = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolutionDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintIssues_SprintReports_SprintReportId",
                        column: x => x.SprintReportId,
                        principalTable: "SprintReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorklogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JiraIssueModelId = table.Column<int>(type: "int", nullable: true),
                    SprintIssueId = table.Column<int>(type: "int", nullable: true),
                    Author = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TimeSpent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Started = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorklogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorklogEntries_JiraIssues_JiraIssueModelId",
                        column: x => x.JiraIssueModelId,
                        principalTable: "JiraIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorklogEntries_SprintIssues_SprintIssueId",
                        column: x => x.SprintIssueId,
                        principalTable: "SprintIssues",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpicSummaries_Key",
                table: "EpicSummaries",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "IX_JiraIssues_JiraEpicReportId",
                table: "JiraIssues",
                column: "JiraEpicReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintIssues_SprintReportId",
                table: "SprintIssues",
                column: "SprintReportId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintReports_ReportIdentifier",
                table: "SprintReports",
                column: "ReportIdentifier",
                unique: true,
                filter: "[ReportIdentifier] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorklogEntries_JiraIssueModelId",
                table: "WorklogEntries",
                column: "JiraIssueModelId");

            migrationBuilder.CreateIndex(
                name: "IX_WorklogEntries_SprintIssueId",
                table: "WorklogEntries",
                column: "SprintIssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpicSummaries");

            migrationBuilder.DropTable(
                name: "JiraFilters");

            migrationBuilder.DropTable(
                name: "WorklogEntries");

            migrationBuilder.DropTable(
                name: "JiraIssues");

            migrationBuilder.DropTable(
                name: "SprintIssues");

            migrationBuilder.DropTable(
                name: "JiraEpicReports");

            migrationBuilder.DropTable(
                name: "SprintReports");
        }
    }
}
