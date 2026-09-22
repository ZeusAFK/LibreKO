using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddSpiritGuardianDailyOps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SpiritGuardianBlackTime",
                table: "UserDailyOps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SpiritGuardianBlueTime",
                table: "UserDailyOps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SpiritGuardianRedTime",
                table: "UserDailyOps",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpiritGuardianBlackTime",
                table: "UserDailyOps");

            migrationBuilder.DropColumn(
                name: "SpiritGuardianBlueTime",
                table: "UserDailyOps");

            migrationBuilder.DropColumn(
                name: "SpiritGuardianRedTime",
                table: "UserDailyOps");
        }
    }
}
