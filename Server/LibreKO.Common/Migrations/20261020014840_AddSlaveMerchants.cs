using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaveMerchants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlaveMerchants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OwnerCharacterId = table.Column<int>(type: "int", nullable: false),
                    OwnerAccountId = table.Column<int>(type: "int", nullable: false),
                    OwnerName = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
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
                    EarnedNoah = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlaveMerchants", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SlaveMerchants_IsActive",
                table: "SlaveMerchants",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SlaveMerchants_OwnerCharacterId",
                table: "SlaveMerchants",
                column: "OwnerCharacterId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlaveMerchants");
        }
    }
}
