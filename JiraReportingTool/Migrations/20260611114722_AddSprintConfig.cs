using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JiraReportingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SprintConfigs",
                columns: table => new
                {
                    Id        = table.Column<int>(type: "int", nullable: false)
                                    .Annotation("SqlServer:Identity", "1, 1"),
                    Name      = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SprintId  = table.Column<int>(type: "int", nullable: false),
                    EpicKey   = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate   = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintConfigs", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SprintConfigs");
        }
    }
}
