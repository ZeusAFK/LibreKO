using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddPets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PetExp",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "PetItemId",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "PetLevel",
                table: "Characters");

            migrationBuilder.DropColumn(
                name: "PetSatisfaction",
                table: "Characters");

            migrationBuilder.CreateTable(
                name: "PetLevels",
                columns: table => new
                {
                    Level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MaxHp = table.Column<short>(type: "smallint", nullable: false),
                    MaxMp = table.Column<short>(type: "smallint", nullable: false),
                    Attack = table.Column<short>(type: "smallint", nullable: false),
                    Defence = table.Column<short>(type: "smallint", nullable: false),
                    Resist = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Exp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PetLevels", x => x.Level);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Pets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Hp = table.Column<short>(type: "smallint", nullable: false),
                    Mp = table.Column<short>(type: "smallint", nullable: false),
                    Satisfaction = table.Column<short>(type: "smallint", nullable: false),
                    Exp = table.Column<long>(type: "bigint", nullable: false),
                    ModelId = table.Column<short>(type: "smallint", nullable: false),
                    Size = table.Column<short>(type: "smallint", nullable: false),
                    Class = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Items = table.Column<byte[]>(type: "longblob", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pets", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Pets_Name",
                table: "Pets",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PetLevels");

            migrationBuilder.DropTable(
                name: "Pets");

            migrationBuilder.AddColumn<long>(
                name: "PetExp",
                table: "Characters",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "PetItemId",
                table: "Characters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "PetLevel",
                table: "Characters",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<short>(
                name: "PetSatisfaction",
                table: "Characters",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);
        }
    }
}
