using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M1_Recipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    TagType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PluralName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Aliases = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    System = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    ToBase = table.Column<double>(type: "REAL", nullable: false),
                    IsVague = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Servings = table.Column<double>(type: "REAL", nullable: false),
                    ServingsEstimated = table.Column<bool>(type: "INTEGER", nullable: false),
                    YieldText = table.Column<string>(type: "TEXT", nullable: true),
                    YieldAmount = table.Column<double>(type: "REAL", nullable: true),
                    YieldUnitId = table.Column<int>(type: "INTEGER", nullable: true),
                    YieldSource = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    FinishedDensityGPerMl = table.Column<double>(type: "REAL", nullable: true),
                    PrepMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    CookMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    TotalMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    IsPrecision = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SourceAuthor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SourceImportedAt = table.Column<string>(type: "TEXT", nullable: true),
                    ParentRecipeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    LastCookedAt = table.Column<string>(type: "TEXT", nullable: true),
                    InventoryVersionSeen = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<string>(type: "TEXT", nullable: true),
                    DeletedById = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recipes_Recipes_ParentRecipeId",
                        column: x => x.ParentRecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Recipes_Units_YieldUnitId",
                        column: x => x.YieldUnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CollectionRecipes",
                columns: table => new
                {
                    CollectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRecipes", x => new { x.CollectionId, x.RecipeId });
                    table.ForeignKey(
                        name: "FK_CollectionRecipes_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionRecipes_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IngredientGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngredientGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IngredientGroups_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeSlugAliases",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeSlugAliases", x => x.Slug);
                    table.ForeignKey(
                        name: "FK_RecipeSlugAliases_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeTags",
                columns: table => new
                {
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeTags", x => new { x.RecipeId, x.TagId });
                    table.ForeignKey(
                        name: "FK_RecipeTags_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Steps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    SectionName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TimerSeconds = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Steps_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupId = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    RawText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Quantity = table.Column<double>(type: "REAL", nullable: true),
                    QuantityMax = table.Column<double>(type: "REAL", nullable: true),
                    QuantityText = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    UnitId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Preparation = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    IsOptional = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ParseConfidence = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_IngredientGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "IngredientGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Units",
                columns: new[] { "Id", "Aliases", "Code", "IsVague", "Kind", "Name", "PluralName", "System", "ToBase" },
                values: new object[,]
                {
                    { 1, "teaspoon|teaspoons|tsp|tsps|tsp.|teasp|teasp.", "tsp", false, "Volume", "tsp", "tsp", "US", 4.9289215937500002 },
                    { 2, "tablespoon|tablespoons|tbsp|tbsps|tbsp.|tbs|tbs.|tbl|tbl.|tblsp|tblsp.", "tbsp", false, "Volume", "Tbsp", "Tbsp", "US", 14.78676478125 },
                    { 3, "cup|cups|c.", "cup", false, "Volume", "cup", "cups", "US", 236.58823649999999 },
                    { 4, "fl oz|fl. oz.|fl.oz.|fl oz.|fl. oz|fluid ounce|fluid ounces|floz", "floz", false, "Volume", "fl oz", "fl oz", "US", 29.573529562499999 },
                    { 5, "pint|pints|pt|pt.|pts", "pint", false, "Volume", "pint", "pints", "US", 473.17647299999999 },
                    { 6, "quart|quarts|qt|qt.|qts", "quart", false, "Volume", "quart", "quarts", "US", 946.35294599999997 },
                    { 7, "gallon|gallons|gal|gal.", "gallon", false, "Volume", "gallon", "gallons", "US", 3785.4117839999999 },
                    { 8, "ml|ml.|milliliter|milliliters|millilitre|millilitres", "ml", false, "Volume", "ml", "ml", "Metric", 1.0 },
                    { 9, "liter|liters|litre|litres|ltr", "l", false, "Volume", "l", "l", "Metric", 1000.0 },
                    { 10, "g|g.|gram|grams|gm|gms|gr", "g", false, "Mass", "g", "g", "Metric", 1.0 },
                    { 11, "kg|kg.|kilogram|kilograms|kilo|kilos", "kg", false, "Mass", "kg", "kg", "Metric", 1000.0 },
                    { 12, "oz|oz.|ounce|ounces", "oz", false, "Mass", "oz", "oz", "US", 28.349523125000001 },
                    { 13, "lb|lb.|lbs|lbs.|pound|pounds", "lb", false, "Mass", "lb", "lb", "US", 453.59237000000002 },
                    { 14, "pinch|pinches", "pinch", true, "Vague", "pinch", "pinches", "Either", 0.30805759960938001 },
                    { 15, "dash|dashes", "dash", true, "Vague", "dash", "dashes", "Either", 0.61611519921875002 },
                    { 16, "smidgen|smidgens|smidge", "smidgen", true, "Vague", "smidgen", "smidgens", "Either", 0.15402879980469 },
                    { 17, "drop|drops", "drop", true, "Vague", "drop", "drops", "Either", 0.050000000000000003 },
                    { 18, "handful|handfuls", "handful", true, "Vague", "handful", "handfuls", "Either", 0.0 },
                    { 19, "can|cans|tin|tins", "can", false, "Count", "can", "cans", "Either", 1.0 },
                    { 20, "jar|jars", "jar", false, "Count", "jar", "jars", "Either", 1.0 },
                    { 21, "package|packages|pkg|pkg.|pkgs|packet|packets|pack|packs", "package", false, "Count", "package", "packages", "Either", 1.0 },
                    { 22, "envelope|envelopes", "envelope", false, "Count", "envelope", "envelopes", "Either", 1.0 },
                    { 23, "bag|bags", "bag", false, "Count", "bag", "bags", "Either", 1.0 },
                    { 24, "box|boxes", "box", false, "Count", "box", "boxes", "Either", 1.0 },
                    { 25, "bottle|bottles", "bottle", false, "Count", "bottle", "bottles", "Either", 1.0 },
                    { 26, "container|containers", "container", false, "Count", "container", "containers", "Either", 1.0 },
                    { 27, "stick|sticks", "stick", false, "Count", "stick", "sticks", "Either", 1.0 },
                    { 28, "clove|cloves", "clove", false, "Count", "clove", "cloves", "Either", 1.0 },
                    { 29, "slice|slices", "slice", false, "Count", "slice", "slices", "Either", 1.0 },
                    { 30, "sprig|sprigs", "sprig", false, "Count", "sprig", "sprigs", "Either", 1.0 },
                    { 31, "bunch|bunches", "bunch", false, "Count", "bunch", "bunches", "Either", 1.0 },
                    { 32, "head|heads", "head", false, "Count", "head", "heads", "Either", 1.0 },
                    { 33, "stalk|stalks", "stalk", false, "Count", "stalk", "stalks", "Either", 1.0 },
                    { 34, "piece|pieces", "piece", false, "Count", "piece", "pieces", "Either", 1.0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRecipes_RecipeId",
                table: "CollectionRecipes",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_NormalizedName",
                table: "Collections",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Slug",
                table: "Collections",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IngredientGroups_RecipeId_SortOrder",
                table: "IngredientGroups",
                columns: new[] { "RecipeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_GroupId",
                table: "RecipeIngredients",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_Name",
                table: "RecipeIngredients",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_RecipeId_SortOrder",
                table: "RecipeIngredients",
                columns: new[] { "RecipeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_UnitId",
                table: "RecipeIngredients",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_CreatedAt",
                table: "Recipes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_ParentRecipeId",
                table: "Recipes",
                column: "ParentRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Slug",
                table: "Recipes",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Status_DeletedAt",
                table: "Recipes",
                columns: new[] { "Status", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_Title",
                table: "Recipes",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_TotalMinutes",
                table: "Recipes",
                column: "TotalMinutes");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_YieldUnitId",
                table: "Recipes",
                column: "YieldUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeSlugAliases_RecipeId",
                table: "RecipeSlugAliases",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeTags_TagId",
                table: "RecipeTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Steps_RecipeId_SortOrder",
                table: "Steps",
                columns: new[] { "RecipeId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_NormalizedName",
                table: "Tags",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Slug",
                table: "Tags",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Units_Code",
                table: "Units",
                column: "Code",
                unique: true);

            // FR-SRCH-01: full-text index of published recipes; rowid = Recipes.Id. EF cannot model virtual
            // tables, so the save pipeline (RecipeSearchIndex) maintains it. unicode61 folds case and accents.
            migrationBuilder.Sql(
                "CREATE VIRTUAL TABLE RecipeSearch USING fts5(Title, Description, Ingredients, Steps, Tags, Notes, " +
                "tokenize = 'unicode61 remove_diacritics 2', prefix = '2 3');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS RecipeSearch;");

            migrationBuilder.DropTable(
                name: "CollectionRecipes");

            migrationBuilder.DropTable(
                name: "RecipeIngredients");

            migrationBuilder.DropTable(
                name: "RecipeSlugAliases");

            migrationBuilder.DropTable(
                name: "RecipeTags");

            migrationBuilder.DropTable(
                name: "Steps");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "IngredientGroups");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Recipes");

            migrationBuilder.DropTable(
                name: "Units");
        }
    }
}
