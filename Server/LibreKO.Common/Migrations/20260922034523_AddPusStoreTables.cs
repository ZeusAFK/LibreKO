using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations;

public partial class AddPusStoreTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PusCategories",
            columns: table => new
            {
                Id = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                Name = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Description = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Status = table.Column<byte>(type: "tinyint unsigned", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PusCategories", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "PusItems",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                ItemId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Description = table.Column<string>(type: "longtext", nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Price = table.Column<int>(type: "int", nullable: false),
                Category = table.Column<byte>(type: "tinyint unsigned", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PusItems", x => x.Id);
            })
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PusItems");
        migrationBuilder.DropTable(name: "PusCategories");
    }
}
