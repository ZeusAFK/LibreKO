using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    public partial class AddKnightsGradeAndPointMethod : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "ClanPointMethod",
                table: "Knights",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte>(
                name: "Grade",
                table: "Knights",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)5);

            migrationBuilder.Sql("UPDATE `Knights` SET `Grade` = CASE WHEN `Ranking` BETWEEN 1 AND 5 THEN `Ranking` ELSE 5 END, `Ranking` = 0;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClanPointMethod",
                table: "Knights");

            migrationBuilder.DropColumn(
                name: "Grade",
                table: "Knights");
        }
    }
}
