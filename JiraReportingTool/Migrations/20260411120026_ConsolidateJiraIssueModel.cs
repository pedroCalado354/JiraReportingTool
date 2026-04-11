using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateJiraIssueModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorklogEntries_JiraIssues_JiraIssueModelId",
                table: "WorklogEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_WorklogEntries_SprintIssues_SprintIssueId",
                table: "WorklogEntries");

            migrationBuilder.DropTable(
                name: "JiraIssues");

            migrationBuilder.DropIndex(
                name: "IX_WorklogEntries_JiraIssueModelId",
                table: "WorklogEntries");

            migrationBuilder.DropColumn(
                name: "JiraIssueModelId",
                table: "WorklogEntries");

            migrationBuilder.AlterColumn<int>(
                name: "SprintReportId",
                table: "SprintIssues",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "JiraEpicReportId",
                table: "SprintIssues",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SprintIssues_JiraEpicReportId",
                table: "SprintIssues",
                column: "JiraEpicReportId");

            migrationBuilder.AddForeignKey(
                name: "FK_SprintIssues_JiraEpicReports_JiraEpicReportId",
                table: "SprintIssues",
                column: "JiraEpicReportId",
                principalTable: "JiraEpicReports",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WorklogEntries_SprintIssues_SprintIssueId",
                table: "WorklogEntries",
                column: "SprintIssueId",
                principalTable: "SprintIssues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SprintIssues_JiraEpicReports_JiraEpicReportId",
                table: "SprintIssues");

            migrationBuilder.DropForeignKey(
                name: "FK_WorklogEntries_SprintIssues_SprintIssueId",
                table: "WorklogEntries");

            migrationBuilder.DropIndex(
                name: "IX_SprintIssues_JiraEpicReportId",
                table: "SprintIssues");

            migrationBuilder.DropColumn(
                name: "JiraEpicReportId",
                table: "SprintIssues");

            migrationBuilder.AddColumn<int>(
                name: "JiraIssueModelId",
                table: "WorklogEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SprintReportId",
                table: "SprintIssues",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "JiraIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Assignee = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssueType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JiraEpicReportId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Labels = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalEstimate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalEstimateSeconds = table.Column<int>(type: "int", nullable: false),
                    RemainingEstimate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StatusCategoryKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TimeSpent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "int", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_WorklogEntries_JiraIssueModelId",
                table: "WorklogEntries",
                column: "JiraIssueModelId");

            migrationBuilder.CreateIndex(
                name: "IX_JiraIssues_JiraEpicReportId",
                table: "JiraIssues",
                column: "JiraEpicReportId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorklogEntries_JiraIssues_JiraIssueModelId",
                table: "WorklogEntries",
                column: "JiraIssueModelId",
                principalTable: "JiraIssues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_WorklogEntries_SprintIssues_SprintIssueId",
                table: "WorklogEntries",
                column: "SprintIssueId",
                principalTable: "SprintIssues",
                principalColumn: "Id");
        }
    }
}
