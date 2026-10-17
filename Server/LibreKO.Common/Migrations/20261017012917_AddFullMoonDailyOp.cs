using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddFullMoonDailyOp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FullMoonRiftTime",
                table: "UserDailyOps",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FullMoonRiftTime",
                table: "UserDailyOps");
        }
    }
}
