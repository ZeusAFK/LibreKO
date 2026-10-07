using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddForgottenTempleWaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForgottenTempleWaves",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Tier = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Wave = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    StartSecond = table.Column<int>(type: "int", nullable: false),
                    NpcId = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForgottenTempleWaves", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ForgottenTempleWaves_Tier_Wave",
                table: "ForgottenTempleWaves",
                columns: new[] { "Tier", "Wave" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForgottenTempleWaves");
        }
    }
}
