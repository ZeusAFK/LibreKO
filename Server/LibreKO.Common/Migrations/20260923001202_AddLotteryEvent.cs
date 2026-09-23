using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddLotteryEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LotteryEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    UserLimit = table.Column<int>(type: "int", nullable: false),
                    ReqItemId = table.Column<int>(type: "int", nullable: false),
                    ReqItemCount = table.Column<int>(type: "int", nullable: false),
                    AutoStart = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotteryEvents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LotteryRewards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LotteryId = table.Column<int>(type: "int", nullable: false),
                    Place = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotteryRewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotteryRewards_LotteryEvents_LotteryId",
                        column: x => x.LotteryId,
                        principalTable: "LotteryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LotterySchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LotteryId = table.Column<int>(type: "int", nullable: false),
                    Day = table.Column<int>(type: "int", nullable: true),
                    Hour = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Minute = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotterySchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LotterySchedules_LotteryEvents_LotteryId",
                        column: x => x.LotteryId,
                        principalTable: "LotteryEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LotteryRewards_LotteryId",
                table: "LotteryRewards",
                column: "LotteryId");

            migrationBuilder.CreateIndex(
                name: "IX_LotterySchedules_LotteryId",
                table: "LotterySchedules",
                column: "LotteryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LotteryRewards");

            migrationBuilder.DropTable(
                name: "LotterySchedules");

            migrationBuilder.DropTable(
                name: "LotteryEvents");
        }
    }
}
