using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddRosterPageInclusionFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeInCommandCenter",
                table: "Roster",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeInDeliveryReports",
                table: "Roster",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeInSprintPlanning",
                table: "Roster",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeInCommandCenter",
                table: "Roster");

            migrationBuilder.DropColumn(
                name: "IncludeInDeliveryReports",
                table: "Roster");

            migrationBuilder.DropColumn(
                name: "IncludeInSprintPlanning",
                table: "Roster");
        }
    }
}
