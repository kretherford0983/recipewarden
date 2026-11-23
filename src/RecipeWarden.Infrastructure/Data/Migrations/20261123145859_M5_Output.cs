using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M5_Output : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackageName",
                table: "Foods",
                type: "TEXT",
                maxLength: 24,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShoppingListNames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    LastUsedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingListNames", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingLists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingChecks",
                columns: table => new
                {
                    ShoppingListId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 202, nullable: false),
                    CheckedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingChecks", x => new { x.ShoppingListId, x.ItemKey });
                    table.ForeignKey(
                        name: "FK_ShoppingChecks_ShoppingLists_ShoppingListId",
                        column: x => x.ShoppingListId,
                        principalTable: "ShoppingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingListRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShoppingListId = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Scale = table.Column<double>(type: "REAL", nullable: false),
                    AddedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingListRecipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingListRecipes_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShoppingListRecipes_ShoppingLists_ShoppingListId",
                        column: x => x.ShoppingListId,
                        principalTable: "ShoppingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShoppingListId = table.Column<int>(type: "INTEGER", nullable: false),
                    ListRecipeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Amount = table.Column<double>(type: "REAL", nullable: true),
                    AmountMax = table.Column<double>(type: "REAL", nullable: true),
                    CountUnit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    FoodId = table.Column<int>(type: "INTEGER", nullable: true),
                    RawText = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingLines_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShoppingLines_ShoppingListRecipes_ListRecipeId",
                        column: x => x.ListRecipeId,
                        principalTable: "ShoppingListRecipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShoppingLines_ShoppingLists_ShoppingListId",
                        column: x => x.ShoppingListId,
                        principalTable: "ShoppingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLines_FoodId",
                table: "ShoppingLines",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLines_ListRecipeId",
                table: "ShoppingLines",
                column: "ListRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLines_ShoppingListId_Position",
                table: "ShoppingLines",
                columns: new[] { "ShoppingListId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingListNames_LastUsedAt",
                table: "ShoppingListNames",
                column: "LastUsedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingListNames_NormalizedName",
                table: "ShoppingListNames",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingListRecipes_RecipeId",
                table: "ShoppingListRecipes",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingListRecipes_ShoppingListId",
                table: "ShoppingListRecipes",
                column: "ShoppingListId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLists_FinishedAt",
                table: "ShoppingLists",
                column: "FinishedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShoppingChecks");

            migrationBuilder.DropTable(
                name: "ShoppingLines");

            migrationBuilder.DropTable(
                name: "ShoppingListNames");

            migrationBuilder.DropTable(
                name: "ShoppingListRecipes");

            migrationBuilder.DropTable(
                name: "ShoppingLists");

            migrationBuilder.DropColumn(
                name: "PackageName",
                table: "Foods");
        }
    }
}
