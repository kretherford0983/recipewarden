using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M6_Nutrition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NutrientRefId",
                table: "Foods",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NutrientDatasets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Version = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FileSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Foods = table.Column<int>(type: "INTEGER", nullable: false),
                    Added = table.Column<int>(type: "INTEGER", nullable: false),
                    Updated = table.Column<int>(type: "INTEGER", nullable: false),
                    Removed = table.Column<int>(type: "INTEGER", nullable: false),
                    AppliedAt = table.Column<string>(type: "TEXT", nullable: false),
                    AppliedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutrientDatasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NutrientRefs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FdcId = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    NormalizedDescription = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DatasetVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RemovedInVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DensityGPerMl = table.Column<double>(type: "REAL", nullable: true),
                    PortionsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Calories = table.Column<double>(type: "REAL", nullable: true),
                    FatG = table.Column<double>(type: "REAL", nullable: true),
                    SatFatG = table.Column<double>(type: "REAL", nullable: true),
                    CholesterolMg = table.Column<double>(type: "REAL", nullable: true),
                    SodiumMg = table.Column<double>(type: "REAL", nullable: true),
                    CarbsG = table.Column<double>(type: "REAL", nullable: true),
                    FiberG = table.Column<double>(type: "REAL", nullable: true),
                    SugarG = table.Column<double>(type: "REAL", nullable: true),
                    ProteinG = table.Column<double>(type: "REAL", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutrientRefs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NutritionFacts",
                columns: table => new
                {
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Calories = table.Column<double>(type: "REAL", nullable: true),
                    FatG = table.Column<double>(type: "REAL", nullable: true),
                    SatFatG = table.Column<double>(type: "REAL", nullable: true),
                    CholesterolMg = table.Column<double>(type: "REAL", nullable: true),
                    SodiumMg = table.Column<double>(type: "REAL", nullable: true),
                    CarbsG = table.Column<double>(type: "REAL", nullable: true),
                    FiberG = table.Column<double>(type: "REAL", nullable: true),
                    SugarG = table.Column<double>(type: "REAL", nullable: true),
                    ProteinG = table.Column<double>(type: "REAL", nullable: true),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NutritionFacts", x => new { x.RecipeId, x.Source });
                    table.ForeignKey(
                        name: "FK_NutritionFacts_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeAllergens",
                columns: table => new
                {
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Allergen = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeAllergens", x => new { x.RecipeId, x.Allergen, x.Origin });
                    table.ForeignKey(
                        name: "FK_RecipeAllergens_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Foods_NutrientRefId",
                table: "Foods",
                column: "NutrientRefId");

            migrationBuilder.CreateIndex(
                name: "IX_NutrientRefs_FdcId",
                table: "NutrientRefs",
                column: "FdcId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NutrientRefs_NormalizedDescription",
                table: "NutrientRefs",
                column: "NormalizedDescription");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeAllergens_Allergen_Status",
                table: "RecipeAllergens",
                columns: new[] { "Allergen", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_Foods_NutrientRefs_NutrientRefId",
                table: "Foods",
                column: "NutrientRefId",
                principalTable: "NutrientRefs",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Foods_NutrientRefs_NutrientRefId",
                table: "Foods");

            migrationBuilder.DropTable(
                name: "NutrientDatasets");

            migrationBuilder.DropTable(
                name: "NutrientRefs");

            migrationBuilder.DropTable(
                name: "NutritionFacts");

            migrationBuilder.DropTable(
                name: "RecipeAllergens");

            migrationBuilder.DropIndex(
                name: "IX_Foods_NutrientRefId",
                table: "Foods");

            migrationBuilder.DropColumn(
                name: "NutrientRefId",
                table: "Foods");
        }
    }
}
