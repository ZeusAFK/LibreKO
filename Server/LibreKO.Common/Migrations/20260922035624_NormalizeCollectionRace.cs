using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeCollectionRace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionRaceSettings");

            migrationBuilder.Sql("DELETE FROM `CollectionRaceRewards`;");

            migrationBuilder.RenameColumn(
                name: "EventIndex",
                table: "CollectionRaceRewards",
                newName: "RaceId");

            migrationBuilder.RenameIndex(
                name: "IX_CollectionRaceRewards_EventIndex",
                table: "CollectionRaceRewards",
                newName: "IX_CollectionRaceRewards_RaceId");

            migrationBuilder.CreateTable(
                name: "CollectionRaces",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ZoneId = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MinLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MaxLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    AutoStart = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRaces", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CollectionRaceObjectives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RaceId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    TargetId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRaceObjectives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionRaceObjectives_CollectionRaces_RaceId",
                        column: x => x.RaceId,
                        principalTable: "CollectionRaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CollectionRaceSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RaceId = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: true),
                    Hour = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Minute = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRaceSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectionRaceSchedules_CollectionRaces_RaceId",
                        column: x => x.RaceId,
                        principalTable: "CollectionRaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRaceObjectives_RaceId_Ordinal",
                table: "CollectionRaceObjectives",
                columns: new[] { "RaceId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRaces_ZoneId",
                table: "CollectionRaces",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionRaceSchedules_RaceId",
                table: "CollectionRaceSchedules",
                column: "RaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_CollectionRaceRewards_CollectionRaces_RaceId",
                table: "CollectionRaceRewards",
                column: "RaceId",
                principalTable: "CollectionRaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CollectionRaceRewards_CollectionRaces_RaceId",
                table: "CollectionRaceRewards");

            migrationBuilder.DropTable(
                name: "CollectionRaceObjectives");

            migrationBuilder.DropTable(
                name: "CollectionRaceSchedules");

            migrationBuilder.DropTable(
                name: "CollectionRaces");

            migrationBuilder.RenameColumn(
                name: "RaceId",
                table: "CollectionRaceRewards",
                newName: "EventIndex");

            migrationBuilder.RenameIndex(
                name: "IX_CollectionRaceRewards_RaceId",
                table: "CollectionRaceRewards",
                newName: "IX_CollectionRaceRewards_EventIndex");

            migrationBuilder.CreateTable(
                name: "CollectionRaceSettings",
                columns: table => new
                {
                    EventIndex = table.Column<int>(type: "int", nullable: false),
                    AutoDays = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutoHours = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AutoStart = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    EnemyKillCount = table.Column<int>(type: "int", nullable: false),
                    EventName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MinLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Target1Count = table.Column<int>(type: "int", nullable: false),
                    Target1ProtoId = table.Column<int>(type: "int", nullable: false),
                    Target2Count = table.Column<int>(type: "int", nullable: false),
                    Target2ProtoId = table.Column<int>(type: "int", nullable: false),
                    Target3Count = table.Column<int>(type: "int", nullable: false),
                    Target3ProtoId = table.Column<int>(type: "int", nullable: false),
                    ZoneId = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionRaceSettings", x => x.EventIndex);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
