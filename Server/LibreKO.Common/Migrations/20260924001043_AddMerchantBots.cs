using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantBots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BotMerchants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    BotName = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Race = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Class = table.Column<short>(type: "smallint", nullable: false),
                    Face = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Hair = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ZoneId = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    X = table.Column<float>(type: "float", nullable: false),
                    Y = table.Column<float>(type: "float", nullable: false),
                    Z = table.Column<float>(type: "float", nullable: false),
                    Direction = table.Column<short>(type: "smallint", nullable: false),
                    MerchantType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    StallTitle = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemsJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EquipmentJson = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BotMerchants", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserBots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(21)", maxLength: 21, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBots", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_UserBots_Name",
                table: "UserBots",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BotMerchants");

            migrationBuilder.DropTable(
                name: "UserBots");
        }
    }
}
