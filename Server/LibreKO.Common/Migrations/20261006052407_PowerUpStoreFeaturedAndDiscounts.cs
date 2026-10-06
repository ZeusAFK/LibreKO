using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class PowerUpStoreFeaturedAndDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "PusItems");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "PusItems");

            migrationBuilder.AddColumn<bool>(
                name: "Featured",
                table: "PusItems",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "Kind",
                table: "Mails",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.Sql("UPDATE Mails SET Kind = 1 WHERE SenderCharacterId IS NULL;");

            migrationBuilder.CreateTable(
                name: "PusDiscounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    PusItemId = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Duration = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PusDiscounts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PusDiscounts_PusItemId",
                table: "PusDiscounts",
                column: "PusItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PusDiscounts");

            migrationBuilder.DropColumn(
                name: "Featured",
                table: "PusItems");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Mails");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "PusItems",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PusItems",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
