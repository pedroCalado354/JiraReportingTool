using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class PersistIssueAnalyticsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Customer",
                table: "SprintIssues",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "DevReadyDate",
                table: "SprintIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedIssueKeys",
                table: "SprintIssues",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Product",
                table: "SprintIssues",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "QaReadyDate",
                table: "SprintIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sprints",
                table: "SprintIssues",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Customer",
                table: "SprintIssues");

            migrationBuilder.DropColumn(
                name: "DevReadyDate",
                table: "SprintIssues");

            migrationBuilder.DropColumn(
                name: "LinkedIssueKeys",
                table: "SprintIssues");

            migrationBuilder.DropColumn(
                name: "Product",
                table: "SprintIssues");

            migrationBuilder.DropColumn(
                name: "QaReadyDate",
                table: "SprintIssues");

            migrationBuilder.DropColumn(
                name: "Sprints",
                table: "SprintIssues");
        }
    }
}
