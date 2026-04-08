using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelFieldIntoJiraModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Labels",
                table: "JiraIssues",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Labels",
                table: "JiraIssues");
        }
    }
}
