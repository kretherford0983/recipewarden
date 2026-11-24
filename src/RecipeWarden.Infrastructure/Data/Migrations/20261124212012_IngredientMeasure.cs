using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class IngredientMeasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Measure",
                table: "RecipeIngredients",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "Auto");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Measure",
                table: "RecipeIngredients");
        }
    }
}
