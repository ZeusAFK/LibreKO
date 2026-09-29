using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations;

public partial class AddGenieOnlineTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "GenieRemainingSeconds", table: "Characters", type: "double",
            nullable: false, defaultValue: 0.0);
        // Convert the unexpired legacy balance before removing its source column.
        migrationBuilder.Sql("""
            UPDATE `Characters`
            SET `GenieRemainingSeconds` = CASE WHEN `GenieExpiry` IS NULL THEN 0
                ELSE GREATEST(0, TIMESTAMPDIFF(MICROSECOND, UTC_TIMESTAMP(6), `GenieExpiry`) / 1000000.0) END;
            """);
        migrationBuilder.DropColumn(name: "GenieExpiry", table: "Characters");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "GenieExpiry", table: "Characters", type: "datetime(6)", nullable: true);
        migrationBuilder.Sql("""
            UPDATE `Characters` SET `GenieExpiry` = CASE
                WHEN `GenieRemainingSeconds` > 0
                THEN TIMESTAMPADD(SECOND, CEIL(`GenieRemainingSeconds`), UTC_TIMESTAMP(6))
                ELSE NULL END;
            """);
        migrationBuilder.DropColumn(name: "GenieRemainingSeconds", table: "Characters");
    }
}
