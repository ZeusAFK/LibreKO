using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyPusStoreSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE `PUS_ITEMS` SET `strItemName` = '' WHERE `strItemName` IS NULL;");

            migrationBuilder.RenameTable(
                name: "PUS_CATEGORY",
                newName: "PusCategories");

            migrationBuilder.RenameTable(
                name: "PUS_ITEMS",
                newName: "PusItems");

            migrationBuilder.RenameColumn(
                name: "ID",
                table: "PusCategories",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "Categoryname",
                table: "PusCategories",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "PusCategories",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "ID",
                table: "PusItems",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "ItemID",
                table: "PusItems",
                newName: "ItemId");

            migrationBuilder.RenameColumn(
                name: "strItemName",
                table: "PusItems",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "strItemDesc",
                table: "PusItems",
                newName: "Description");

            migrationBuilder.DropColumn(
                name: "CategoryID",
                table: "PusCategories");

            migrationBuilder.DropColumn(
                name: "strItemTitle",
                table: "PusItems");

            migrationBuilder.DropColumn(
                name: "SendType",
                table: "PusItems");

            migrationBuilder.DropColumn(
                name: "BuyCount",
                table: "PusItems");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PusCategories",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "PusCategories",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PusItems",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(150)",
                oldMaxLength: 150,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "PusItems",
                type: "longtext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "PusItems",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "PusItems",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "PusItems",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PusItems",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "PusCategories",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PusCategories",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "longtext");

            migrationBuilder.AddColumn<byte>(
                name: "CategoryID",
                table: "PusCategories",
                type: "tinyint unsigned",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<int>(
                name: "BuyCount",
                table: "PusItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SendType",
                table: "PusItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "strItemTitle",
                table: "PusItems",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "PusCategories",
                newName: "ID");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "PusCategories",
                newName: "Categoryname");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "PusCategories",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "PusItems",
                newName: "ID");

            migrationBuilder.RenameColumn(
                name: "ItemId",
                table: "PusItems",
                newName: "ItemID");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "PusItems",
                newName: "strItemName");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "PusItems",
                newName: "strItemDesc");

            migrationBuilder.RenameTable(
                name: "PusCategories",
                newName: "PUS_CATEGORY");

            migrationBuilder.RenameTable(
                name: "PusItems",
                newName: "PUS_ITEMS");

        }
    }
}
