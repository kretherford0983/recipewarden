using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeWarden.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class M4_Import : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageId",
                table: "Steps",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinishImageId",
                table: "Recipes",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ThumbBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AltText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BatchId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    SourceRef = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RawPath = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ParsedJson = table.Column<string>(type: "TEXT", nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CopyImage = table.Column<bool>(type: "INTEGER", nullable: false),
                    DuplicateOfRecipeId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedRecipeId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedById = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportJobs_Recipes_CreatedRecipeId",
                        column: x => x.CreatedRecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportJobs_Recipes_DuplicateOfRecipeId",
                        column: x => x.DuplicateOfRecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Steps_ImageId",
                table: "Steps",
                column: "ImageId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_FinishImageId",
                table: "Recipes",
                column: "FinishImageId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_CreatedAt",
                table: "Images",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_BatchId",
                table: "ImportJobs",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_CreatedRecipeId",
                table: "ImportJobs",
                column: "CreatedRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_DuplicateOfRecipeId",
                table: "ImportJobs",
                column: "DuplicateOfRecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportJobs_Status_CreatedAt",
                table: "ImportJobs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Recipes_Images_FinishImageId",
                table: "Recipes",
                column: "FinishImageId",
                principalTable: "Images",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Steps_Images_ImageId",
                table: "Steps",
                column: "ImageId",
                principalTable: "Images",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Recipes_Images_FinishImageId",
                table: "Recipes");

            migrationBuilder.DropForeignKey(
                name: "FK_Steps_Images_ImageId",
                table: "Steps");

            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "ImportJobs");

            migrationBuilder.DropIndex(
                name: "IX_Steps_ImageId",
                table: "Steps");

            migrationBuilder.DropIndex(
                name: "IX_Recipes_FinishImageId",
                table: "Recipes");

            migrationBuilder.DropColumn(
                name: "ImageId",
                table: "Steps");

            migrationBuilder.DropColumn(
                name: "FinishImageId",
                table: "Recipes");
        }
    }
}
