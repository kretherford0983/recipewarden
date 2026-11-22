using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M2_Measurement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DefaultScale",
                table: "UserPreferences",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Units",
                table: "UserPreferences",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Steps",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FoodId",
                table: "RecipeIngredients",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Foods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PluralName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    DensityGPerMl = table.Column<double>(type: "REAL", nullable: true),
                    FdcId = table.Column<int>(type: "INTEGER", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DefaultPurchaseUnitId = table.Column<int>(type: "INTEGER", nullable: true),
                    PackageSize = table.Column<double>(type: "REAL", nullable: true),
                    PackageUnitId = table.Column<int>(type: "INTEGER", nullable: true),
                    StoreSection = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IsPantryStaple = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Foods_Units_DefaultPurchaseUnitId",
                        column: x => x.DefaultPurchaseUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Foods_Units_PackageUnitId",
                        column: x => x.PackageUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeedRuns",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AppliedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedRuns", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "FoodAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FoodId = table.Column<int>(type: "INTEGER", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedAlias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoodAliases_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "YieldConversions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FoodId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromState = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ToState = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    FromUnitKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Ratio = table.Column<double>(type: "REAL", nullable: false),
                    RatioMax = table.Column<double>(type: "REAL", nullable: false),
                    PreparedDensityGPerMl = table.Column<double>(type: "REAL", nullable: true),
                    Basis = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YieldConversions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_YieldConversions_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_FoodId",
                table: "RecipeIngredients",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodAliases_FoodId",
                table: "FoodAliases",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_FoodAliases_NormalizedAlias",
                table: "FoodAliases",
                column: "NormalizedAlias",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Foods_DefaultPurchaseUnitId",
                table: "Foods",
                column: "DefaultPurchaseUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Foods_NormalizedName",
                table: "Foods",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Foods_PackageUnitId",
                table: "Foods",
                column: "PackageUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_YieldConversions_FoodId_FromState",
                table: "YieldConversions",
                columns: new[] { "FoodId", "FromState" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RecipeIngredients_Foods_FoodId",
                table: "RecipeIngredients",
                column: "FoodId",
                principalTable: "Foods",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecipeIngredients_Foods_FoodId",
                table: "RecipeIngredients");

            migrationBuilder.DropTable(
                name: "FoodAliases");

            migrationBuilder.DropTable(
                name: "SeedRuns");

            migrationBuilder.DropTable(
                name: "YieldConversions");

            migrationBuilder.DropTable(
                name: "Foods");

            migrationBuilder.DropIndex(
                name: "IX_RecipeIngredients_FoodId",
                table: "RecipeIngredients");

            migrationBuilder.DropColumn(
                name: "DefaultScale",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "Units",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Steps");

            migrationBuilder.DropColumn(
                name: "FoodId",
                table: "RecipeIngredients");
        }
    }
}
