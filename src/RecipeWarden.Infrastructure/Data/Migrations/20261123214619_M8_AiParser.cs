using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M8_AiParser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ParsedByAi",
                table: "ImportJobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "AiCallLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    At = table.Column<string>(type: "TEXT", nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCallLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCallLogs_At",
                table: "AiCallLogs",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCallLogs");

            migrationBuilder.DropColumn(
                name: "ParsedByAi",
                table: "ImportJobs");
        }
    }
}
