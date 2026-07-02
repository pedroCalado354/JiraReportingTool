using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlaSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SnapshotDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlaSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlaSnapshots_SnapshotDate",
                table: "SlaSnapshots",
                column: "SnapshotDate",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlaSnapshots");
        }
    }
}
