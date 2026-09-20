using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations;

[Migration("20260920000000_AddUsdBalance")]
[DbContext(typeof(AppDbContext))]
public partial class AddUsdBalance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "UsdBalance",
            table: "Accounts",
            type: "int",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "UsdBalance", table: "Accounts");
    }
}
