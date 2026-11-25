using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M1_RangesAndDefaultTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CookMinutesMax",
                table: "Recipes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrepMinutesMax",
                table: "Recipes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ServingsMax",
                table: "Recipes",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalMinutesMax",
                table: "Recipes",
                type: "INTEGER",
                nullable: true);

            // ADR 0011: built-in tags, seeded once. A tag that already has the name (or the slug) is kept and only
            // gets the type if it has none. Fixed text on purpose: this migration must not change if
            // Core.Organization.DefaultTags changes later (that needs a new migration).
            foreach (var (name, normalized, slug, type) in DefaultTagRows)
            {
                migrationBuilder.Sql(
                    "INSERT INTO Tags (Name, NormalizedName, Slug, TagType, CreatedAt, UpdatedAt) " +
                    $"SELECT '{name}', '{normalized}', '{slug}', '{type}', '{SeededAt}', '{SeededAt}' " +
                    $"WHERE NOT EXISTS (SELECT 1 FROM Tags WHERE NormalizedName = '{normalized}' OR Slug = '{slug}');");
                migrationBuilder.Sql(
                    $"UPDATE Tags SET TagType = '{type}' WHERE NormalizedName = '{normalized}' AND TagType IS NULL;");
            }
        }

        private const string SeededAt = "2026-11-21T00:00:00.0000000+00:00";

        // Constant data only (no quotes in any value); never user input.
        private static readonly (string Name, string Normalized, string Slug, string Type)[] DefaultTagRows =
        [
            ("Breakfast", "breakfast", "breakfast", "Meal"),
            ("Brunch", "brunch", "brunch", "Meal"),
            ("Lunch", "lunch", "lunch", "Meal"),
            ("Dinner", "dinner", "dinner", "Meal"),
            ("Snack", "snack", "snack", "Meal"),
            ("Appetizer", "appetizer", "appetizer", "Course"),
            ("Entree", "entree", "entree", "Course"),
            ("Side Dish", "side dish", "side-dish", "Course"),
            ("Soup", "soup", "soup", "Course"),
            ("Salad", "salad", "salad", "Course"),
            ("Dessert", "dessert", "dessert", "Course"),
            ("Bread", "bread", "bread", "Course"),
            ("Sauce", "sauce", "sauce", "Course"),
            ("Drink", "drink", "drink", "Course"),
            ("Homemade Ingredient", "homemade ingredient", "homemade-ingredient", "Course"),
            ("Spring", "spring", "spring", "Season"),
            ("Summer", "summer", "summer", "Season"),
            ("Fall", "fall", "fall", "Season"),
            ("Winter", "winter", "winter", "Season"),
            ("Holiday", "holiday", "holiday", "Season"),
            ("Slow Cooker", "slow cooker", "slow-cooker", "Method"),
            ("Pressure Cooker", "pressure cooker", "pressure-cooker", "Method"),
            ("Air Fryer", "air fryer", "air-fryer", "Method"),
            ("Grill", "grill", "grill", "Method"),
            ("Sheet Pan", "sheet pan", "sheet-pan", "Method"),
            ("One Pot", "one pot", "one-pot", "Method"),
            ("No Cook", "no cook", "no-cook", "Method"),
            ("Vegetarian", "vegetarian", "vegetarian", "Diet"),
            ("Vegan", "vegan", "vegan", "Diet"),
            ("Gluten-Free", "gluten-free", "gluten-free", "Diet"),
            ("Dairy-Free", "dairy-free", "dairy-free", "Diet"),
        ];

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The seeded tags are left in place: by now they may be in use or renamed, and removing them would
            // drop household data.
            migrationBuilder.DropColumn(
                name: "CookMinutesMax",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "PrepMinutesMax",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "ServingsMax",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "TotalMinutesMax",
                table: "Recipes");
        }
    }
}
