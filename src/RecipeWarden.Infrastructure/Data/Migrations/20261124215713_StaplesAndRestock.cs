using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaplesAndRestock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRestock",
                table: "ShoppingLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaple",
                table: "ShoppingLines",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // BR-48: lines already on open lists keep today's routing (staple foods wait under "Check before you buy").
            migrationBuilder.Sql("UPDATE ShoppingLines SET IsStaple = 1 WHERE FoodId IN (SELECT Id FROM Foods WHERE IsPantryStaple = 1);");

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "ShoppingLines",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShoppingStapleChecks",
                columns: table => new
                {
                    ShoppingListId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemKey = table.Column<string>(type: "TEXT", maxLength: 202, nullable: false),
                    Answer = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    At = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingStapleChecks", x => new { x.ShoppingListId, x.ItemKey });
                    table.ForeignKey(
                        name: "FK_ShoppingStapleChecks_ShoppingLists_ShoppingListId",
                        column: x => x.ShoppingListId,
                        principalTable: "ShoppingLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Staples",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FoodId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Restock = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Section = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    LastRestockedAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Staples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Staples_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Staples_FoodId",
                table: "Staples",
                column: "FoodId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staples_NormalizedName",
                table: "Staples",
                column: "NormalizedName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShoppingStapleChecks");

            migrationBuilder.DropTable(
                name: "Staples");

            migrationBuilder.DropColumn(
                name: "IsRestock",
                table: "ShoppingLines");

            migrationBuilder.DropColumn(
                name: "IsStaple",
                table: "ShoppingLines");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "ShoppingLines");
        }
    }
}
