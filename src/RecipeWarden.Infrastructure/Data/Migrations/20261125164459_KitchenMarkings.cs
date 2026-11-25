using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class KitchenMarkings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Markings",
                table: "KitchenTools",
                type: "TEXT",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Markings",
                table: "KitchenTools");
        }
    }
}
