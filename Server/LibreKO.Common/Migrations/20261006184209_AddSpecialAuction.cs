using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialAuction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuctionBids",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Serial = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Slot = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<short>(type: "smallint", nullable: false),
                    CharacterId = table.Column<int>(type: "int", nullable: false),
                    CharacterName = table.Column<string>(type: "varchar(21)", maxLength: 21, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Coins = table.Column<long>(type: "bigint", nullable: false),
                    Checks = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    PlacedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionBids", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuctionResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Serial = table.Column<int>(type: "int", nullable: false),
                    Slot = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SettledAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuctionResults", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SpecialAuctionLots",
                columns: table => new
                {
                    Row = table.Column<int>(type: "int", nullable: false),
                    Slot = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    StartPrice = table.Column<long>(type: "bigint", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    Secret = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpecialAuctionLots", x => new { x.Row, x.Slot });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AuctionBids_Channel_Serial_Slot",
                table: "AuctionBids",
                columns: new[] { "Channel", "Serial", "Slot" });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionBids_CharacterId_ClosedAt",
                table: "AuctionBids",
                columns: new[] { "CharacterId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuctionResults_Channel_Serial",
                table: "AuctionResults",
                columns: new[] { "Channel", "Serial" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuctionBids");

            migrationBuilder.DropTable(
                name: "AuctionResults");

            migrationBuilder.DropTable(
                name: "SpecialAuctionLots");
        }
    }
}
