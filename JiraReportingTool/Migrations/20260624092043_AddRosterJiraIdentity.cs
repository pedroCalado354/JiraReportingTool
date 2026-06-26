using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterJiraIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorAccountId",
                table: "WorklogEntries",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AuthorEmail",
                table: "WorklogEntries",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "JiraAccountId",
                table: "Roster",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JiraEmail",
                table: "Roster",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorAccountId",
                table: "WorklogEntries");

            migrationBuilder.DropColumn(
                name: "AuthorEmail",
                table: "WorklogEntries");

            migrationBuilder.DropColumn(
                name: "JiraAccountId",
                table: "Roster");

            migrationBuilder.DropColumn(
                name: "JiraEmail",
                table: "Roster");
        }
    }
}
