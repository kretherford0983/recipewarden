using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M3_Kitchen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PanLength",
                table: "Recipes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PanShape",
                table: "Recipes",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PanWidth",
                table: "Recipes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Form",
                table: "Foods",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Other"); // ADR 0018: the food-form seed sets the real form at startup

            migrationBuilder.CreateTable(
                name: "InventorySnapshots",
                columns: table => new
                {
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ToolsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventorySnapshots", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "InventoryVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KitchenTools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ToolType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    QuantityOwned = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultFillPct = table.Column<double>(type: "REAL", nullable: false),
                    CapacityAmount = table.Column<double>(type: "REAL", nullable: true),
                    CapacityUnitId = table.Column<int>(type: "INTEGER", nullable: true),
                    ResolutionAmount = table.Column<double>(type: "REAL", nullable: true),
                    ResolutionUnitId = table.Column<int>(type: "INTEGER", nullable: true),
                    SupportedUnits = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    PanShape = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    PanWidth = table.Column<double>(type: "REAL", nullable: true),
                    PanLength = table.Column<double>(type: "REAL", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<string>(type: "TEXT", nullable: false),
                    RetiredAt = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenTools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KitchenTools_Units_CapacityUnitId",
                        column: x => x.CapacityUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitchenTools_Units_ResolutionUnitId",
                        column: x => x.ResolutionUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecipeSuggestionStates",
                columns: table => new
                {
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    SuggestionKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    InventoryVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    At = table.Column<string>(type: "TEXT", nullable: false),
                    ById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeSuggestionStates", x => new { x.RecipeId, x.SuggestionKey });
                    table.ForeignKey(
                        name: "FK_RecipeSuggestionStates_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTools_CapacityUnitId",
                table: "KitchenTools",
                column: "CapacityUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTools_IsActive_ToolType",
                table: "KitchenTools",
                columns: new[] { "IsActive", "ToolType" });

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTools_ResolutionUnitId",
                table: "KitchenTools",
                column: "ResolutionUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventorySnapshots");

            migrationBuilder.DropTable(
                name: "InventoryVersions");

            migrationBuilder.DropTable(
                name: "KitchenTools");

            migrationBuilder.DropTable(
                name: "RecipeSuggestionStates");

            migrationBuilder.DropColumn(
                name: "PanLength",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "PanShape",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "PanWidth",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "Form",
                table: "Foods");
        }
    }
}
