using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddDrakiEntranceLimitToCharacter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "DrakiEntranceLimit",
                table: "Characters",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)3);

            migrationBuilder.AddColumn<DateTime>(
                name: "DrakiEntranceLimitResetDate",
                table: "Characters",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DrakiEntranceLimit",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "DrakiEntranceLimitResetDate",
                table: "Characters");
        }
    }
}
