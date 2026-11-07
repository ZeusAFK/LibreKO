using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddRentalListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RentalListings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    OwnerCharacterId = table.Column<int>(type: "int", nullable: false),
                    OwnerName = table.Column<string>(type: "varchar(21)", maxLength: 21, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Durability = table.Column<short>(type: "smallint", nullable: false),
                    Flag = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    UniqueId = table.Column<int>(type: "int", nullable: false),
                    DailyPrice = table.Column<int>(type: "int", nullable: false),
                    ListedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ListingEndsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RenterCharacterId = table.Column<int>(type: "int", nullable: false),
                    RentedUntil = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Earnings = table.Column<long>(type: "bigint", nullable: false),
                    Collected = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RentalListings", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RentalListings_ListingEndsAt",
                table: "RentalListings",
                column: "ListingEndsAt");

            migrationBuilder.CreateIndex(
                name: "IX_RentalListings_OwnerCharacterId",
                table: "RentalListings",
                column: "OwnerCharacterId");

            migrationBuilder.Sql(
                "INSERT INTO `NpcPositions` (`Index`, `ZoneId`, `NpcId`, `ActType`, `DotCnt`, `Path`, `LeftX`, `TopZ`, `NumNPC`, `RegTime`, `Direction`, `SpawnRange`, `RegenType`, `DungeonFamily`, `SpecialType`, `TrapNumber`, `Room`) " +
                "SELECT 8322, 21, 14401, 100, 0, NULL, 790, 625, 1, 30, 90, 0, 0, 0, 0, 0, 0 FROM DUAL " +
                "WHERE EXISTS (SELECT 1 FROM `NpcPositions`) " +
                "AND NOT EXISTS (SELECT 1 FROM `NpcPositions` WHERE `Index` = 8322 OR (`ZoneId` = 21 AND `NpcId` = 14401));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM `NpcPositions` WHERE `Index` = 8322 AND `ZoneId` = 21 AND `NpcId` = 14401;");

            migrationBuilder.DropTable(
                name: "RentalListings");
        }
    }
}
