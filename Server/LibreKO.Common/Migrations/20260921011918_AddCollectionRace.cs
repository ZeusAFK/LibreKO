using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionRace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CollectionRaceRewards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    EventIndex = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRaceRewards", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CollectionRaceSettings",
                columns: table => new
                {
                    EventIndex = table.Column<int>(type: "int", nullable: false),
                    EventName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ZoneId = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MinLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MaxLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Target1ProtoId = table.Column<int>(type: "int", nullable: false),
                    Target1Count = table.Column<int>(type: "int", nullable: false),
                    Target2ProtoId = table.Column<int>(type: "int", nullable: false),
                    Target2Count = table.Column<int>(type: "int", nullable: false),
                    Target3ProtoId = table.Column<int>(type: "int", nullable: false),
                    Target3Count = table.Column<int>(type: "int", nullable: false),
                    EnemyKillCount = table.Column<int>(type: "int", nullable: false),
                    AutoStart = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AutoHours = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutoDays = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRaceSettings", x => x.EventIndex);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRaceRewards_EventIndex",
                table: "CollectionRaceRewards",
                column: "EventIndex");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionRaceRewards");

            migrationBuilder.DropTable(
                name: "CollectionRaceSettings");
        }
    }
}
