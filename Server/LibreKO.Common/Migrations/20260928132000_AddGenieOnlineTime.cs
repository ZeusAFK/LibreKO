using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations;

public partial class AddGenieOnlineTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "GenieRemainingSeconds", table: "Characters", type: "double", nullable: true);
        // Freeze the unexpired legacy balance at upgrade time, even for offline users.
        migrationBuilder.Sql("""
            UPDATE `Characters`
            SET `GenieRemainingSeconds` = CASE WHEN `GenieExpiry` IS NULL THEN 0
                ELSE GREATEST(0, TIMESTAMPDIFF(MICROSECOND, UTC_TIMESTAMP(6), `GenieExpiry`) / 1000000.0) END,
                `GenieExpiry` = NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE `Characters` SET `GenieExpiry` = CASE
                WHEN COALESCE(`GenieRemainingSeconds`, 0) > 0
                THEN TIMESTAMPADD(SECOND, CEIL(`GenieRemainingSeconds`), UTC_TIMESTAMP(6))
                ELSE NULL END;
            """);
        migrationBuilder.DropColumn(name: "GenieRemainingSeconds", table: "Characters");
    }
}
