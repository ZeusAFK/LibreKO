using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddItemCombineRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ItemCombineMaterials",
                columns: table => new
                {
                    RecipeId = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCombineMaterials", x => new { x.RecipeId, x.Position });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemCombineRecipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    NpcId = table.Column<int>(type: "int", nullable: false),
                    DisplayRow = table.Column<short>(type: "smallint", nullable: false),
                    ResultItemId = table.Column<int>(type: "int", nullable: false),
                    ResultCount = table.Column<int>(type: "int", nullable: false),
                    SuccessRate = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemCombineRecipes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemCombineMaterials");

            migrationBuilder.DropTable(
                name: "ItemCombineRecipes");
        }
    }
}
