using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddTempleEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JuraidMountainRewards");

            migrationBuilder.DropTable(
                name: "JuraidMountainSchedules");

            migrationBuilder.CreateTable(
                name: "TempleEventRewards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Event = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    MinLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MaxLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    LoyaltyPoints = table.Column<int>(type: "int", nullable: false),
                    ExpPercent = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempleEventRewards", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TempleEventSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Event = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: true),
                    Hour = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Minute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MinLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)20),
                    MaxLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)83),
                    CountdownMinutes = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)10)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TempleEventSchedules", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_TempleEventRewards_Event_Outcome",
                table: "TempleEventRewards",
                columns: new[] { "Event", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_TempleEventSchedules_Event",
                table: "TempleEventSchedules",
                column: "Event");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TempleEventRewards");

            migrationBuilder.DropTable(
                name: "TempleEventSchedules");

            migrationBuilder.CreateTable(
                name: "JuraidMountainRewards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LoyaltyPoints = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JuraidMountainRewards", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "JuraidMountainSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CountdownMinutes = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)10),
                    Day = table.Column<int>(type: "int", nullable: true),
                    Hour = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MaxLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)83),
                    MinLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false, defaultValue: (byte)40),
                    Minute = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JuraidMountainSchedules", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_JuraidMountainRewards_Outcome",
                table: "JuraidMountainRewards",
                column: "Outcome");
        }
    }
}
