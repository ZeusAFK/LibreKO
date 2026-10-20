using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class AddItemIdentityToMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "bFlag",
                table: "MailBoxes",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<long>(
                name: "nExpiresAt",
                table: "MailBoxes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ExpiresAt",
                table: "MailAttachments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<byte>(
                name: "Flag",
                table: "MailAttachments",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "UniqueId",
                table: "MailAttachments",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bFlag",
                table: "MailBoxes");

            migrationBuilder.DropColumn(
                name: "nExpiresAt",
                table: "MailBoxes");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "MailAttachments");

            migrationBuilder.DropColumn(
                name: "Flag",
                table: "MailAttachments");

            migrationBuilder.DropColumn(
                name: "UniqueId",
                table: "MailAttachments");
        }
    }
}
