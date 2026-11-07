using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    public partial class AddAccountPremiums : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountPremiums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Expiry = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountPremiums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountPremiums_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AccountPremiums_AccountId_Type",
                table: "AccountPremiums",
                columns: new[] { "AccountId", "Type" },
                unique: true);

            migrationBuilder.Sql(
                "INSERT INTO AccountPremiums (AccountId, Type, Expiry) " +
                "SELECT Id, PremiumType, PremiumDate FROM Accounts WHERE PremiumType > 0 AND PremiumDate IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "PremiumDate",
                table: "Accounts");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumDate",
                table: "Accounts",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE Accounts a JOIN AccountPremiums p ON p.AccountId = a.Id AND p.Type = a.PremiumType " +
                "SET a.PremiumDate = p.Expiry");

            migrationBuilder.DropTable(
                name: "AccountPremiums");
        }
    }
}
