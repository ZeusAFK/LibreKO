using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations;

[Migration("20260919000000_AddPusStoreTables")]
[DbContext(typeof(AppDbContext))]
public partial class AddPusStoreTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PUS_CATEGORY",
            columns: table => new
            {
                ID = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                Categoryname = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Description = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                CategoryID = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                status = table.Column<byte>(type: "tinyint unsigned", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PUS_CATEGORY", x => x.ID);
            })
            .Annotation("MySql:CharSet", "utf8mb4");

        migrationBuilder.CreateTable(
            name: "PUS_ITEMS",
            columns: table => new
            {
                ID = table.Column<int>(type: "int", nullable: false),
                ItemID = table.Column<int>(type: "int", nullable: false),
                strItemName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                strItemTitle = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Price = table.Column<int>(type: "int", nullable: false),
                SendType = table.Column<int>(type: "int", nullable: false),
                BuyCount = table.Column<int>(type: "int", nullable: false),
                strItemDesc = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                    .Annotation("MySql:CharSet", "utf8mb4"),
                Category = table.Column<byte>(type: "tinyint unsigned", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PUS_ITEMS", x => x.ID);
            })
            .Annotation("MySql:CharSet", "utf8mb4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PUS_ITEMS");
        migrationBuilder.DropTable(name: "PUS_CATEGORY");
    }
}