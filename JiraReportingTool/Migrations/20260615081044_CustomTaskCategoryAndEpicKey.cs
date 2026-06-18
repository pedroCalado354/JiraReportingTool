using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class CustomTaskCategoryAndEpicKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultHours",
                table: "CustomTaskTemplates");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "CustomTaskTemplates");

            migrationBuilder.AddColumn<string>(
                name: "EpicKey",
                table: "SprintPlanCustomTasks",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpicKey",
                table: "SprintPlanCustomTasks");

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultHours",
                table: "CustomTaskTemplates",
                type: "decimal(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "CustomTaskTemplates",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }
    }
}
