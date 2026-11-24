using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M9_FoodSuggestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FoodSuggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NutrientRefId = table.Column<int>(type: "INTEGER", nullable: true),
                    FdcId = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Confidence = table.Column<double>(type: "REAL", nullable: true),
                    EngineVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DatasetVersion = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    DismissedAt = table.Column<string>(type: "TEXT", nullable: true),
                    DismissedById = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodSuggestions_NutrientRefs_NutrientRefId",
                        column: x => x.NutrientRefId,
                        principalTable: "NutrientRefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FoodSuggestions_NormalizedName_EngineVersion_DatasetVersion",
                table: "FoodSuggestions",
                columns: new[] { "NormalizedName", "EngineVersion", "DatasetVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FoodSuggestions_NutrientRefId",
                table: "FoodSuggestions",
                column: "NutrientRefId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoodSuggestions");
        }
    }
}
