using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceItemsWithBasesAndVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemGradeBonuses");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.CreateTable(
                name: "ItemBases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VariantSet = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Slot = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Race = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Class = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Damage = table.Column<short>(type: "smallint", nullable: false),
                    Delay = table.Column<short>(type: "smallint", nullable: false),
                    Range = table.Column<short>(type: "smallint", nullable: false),
                    Weight = table.Column<short>(type: "smallint", nullable: false),
                    Duration = table.Column<short>(type: "smallint", nullable: false),
                    BuyPrice = table.Column<int>(type: "int", nullable: false),
                    SaleType = table.Column<int>(type: "int", nullable: false),
                    Ac = table.Column<short>(type: "smallint", nullable: false),
                    Countable = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Effect1 = table.Column<int>(type: "int", nullable: false),
                    Effect2 = table.Column<int>(type: "int", nullable: false),
                    ReqLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReqLevelMax = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReqRank = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReqTitle = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReqStr = table.Column<short>(type: "smallint", nullable: false),
                    ReqSta = table.Column<short>(type: "smallint", nullable: false),
                    ReqDex = table.Column<short>(type: "smallint", nullable: false),
                    ReqIntel = table.Column<short>(type: "smallint", nullable: false),
                    ReqCha = table.Column<short>(type: "smallint", nullable: false),
                    SellingGroup = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ItemClass = table.Column<short>(type: "smallint", nullable: false),
                    UnbindStones = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemBases", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemVariants",
                columns: table => new
                {
                    VariantSet = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MadeFor = table.Column<int>(type: "int", nullable: false),
                    Rarity = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Damage = table.Column<short>(type: "smallint", nullable: false),
                    Hitrate = table.Column<short>(type: "smallint", nullable: false),
                    Evasionrate = table.Column<short>(type: "smallint", nullable: false),
                    Duration = table.Column<short>(type: "smallint", nullable: false),
                    PriceMultiplier = table.Column<short>(type: "smallint", nullable: false),
                    Ac = table.Column<short>(type: "smallint", nullable: false),
                    DaggerAc = table.Column<short>(type: "smallint", nullable: false),
                    JamadarAc = table.Column<short>(type: "smallint", nullable: false),
                    SwordAc = table.Column<short>(type: "smallint", nullable: false),
                    MaceAc = table.Column<short>(type: "smallint", nullable: false),
                    AxeAc = table.Column<short>(type: "smallint", nullable: false),
                    SpearAc = table.Column<short>(type: "smallint", nullable: false),
                    BowAc = table.Column<short>(type: "smallint", nullable: false),
                    FireDamage = table.Column<short>(type: "smallint", nullable: false),
                    IceDamage = table.Column<short>(type: "smallint", nullable: false),
                    LightningDamage = table.Column<short>(type: "smallint", nullable: false),
                    PoisonDamage = table.Column<short>(type: "smallint", nullable: false),
                    HpDrain = table.Column<short>(type: "smallint", nullable: false),
                    MpDamage = table.Column<short>(type: "smallint", nullable: false),
                    MpDrain = table.Column<short>(type: "smallint", nullable: false),
                    MirrorDamage = table.Column<short>(type: "smallint", nullable: false),
                    GradeClass = table.Column<short>(type: "smallint", nullable: false),
                    Str = table.Column<short>(type: "smallint", nullable: false),
                    Sta = table.Column<short>(type: "smallint", nullable: false),
                    Dex = table.Column<short>(type: "smallint", nullable: false),
                    Intel = table.Column<short>(type: "smallint", nullable: false),
                    Cha = table.Column<short>(type: "smallint", nullable: false),
                    MaxHp = table.Column<short>(type: "smallint", nullable: false),
                    MaxMp = table.Column<short>(type: "smallint", nullable: false),
                    FireR = table.Column<short>(type: "smallint", nullable: false),
                    ColdR = table.Column<short>(type: "smallint", nullable: false),
                    LightningR = table.Column<short>(type: "smallint", nullable: false),
                    MagicR = table.Column<short>(type: "smallint", nullable: false),
                    PoisonR = table.Column<short>(type: "smallint", nullable: false),
                    CurseR = table.Column<short>(type: "smallint", nullable: false),
                    Plus = table.Column<int>(type: "int", nullable: false),
                    ReqLevel = table.Column<short>(type: "smallint", nullable: false),
                    ReqTitle = table.Column<short>(type: "smallint", nullable: false),
                    ReqStr = table.Column<short>(type: "smallint", nullable: false),
                    ReqSta = table.Column<short>(type: "smallint", nullable: false),
                    ReqDex = table.Column<short>(type: "smallint", nullable: false),
                    ReqIntel = table.Column<short>(type: "smallint", nullable: false),
                    ReqCha = table.Column<short>(type: "smallint", nullable: false),
                    UnbindStones = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemVariants", x => new { x.VariantSet, x.Id });
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemBases");

            migrationBuilder.DropTable(
                name: "ItemVariants");

            migrationBuilder.CreateTable(
                name: "ItemGradeBonuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Bonus = table.Column<int>(type: "int", nullable: false),
                    ExtFile = table.Column<int>(type: "int", nullable: false),
                    ExtensionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemGradeBonuses", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Num = table.Column<int>(type: "int", nullable: false),
                    Ac = table.Column<short>(type: "smallint", nullable: false),
                    AxeAc = table.Column<short>(type: "smallint", nullable: false),
                    Bound = table.Column<short>(type: "smallint", nullable: false),
                    BowAc = table.Column<short>(type: "smallint", nullable: false),
                    BuyPrice = table.Column<int>(type: "int", nullable: false),
                    ChaB = table.Column<short>(type: "smallint", nullable: false),
                    Class = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ColdR = table.Column<short>(type: "smallint", nullable: false),
                    Countable = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    CurseR = table.Column<short>(type: "smallint", nullable: false),
                    DaggerAc = table.Column<short>(type: "smallint", nullable: false),
                    Damage = table.Column<short>(type: "smallint", nullable: false),
                    Delay = table.Column<short>(type: "smallint", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DexB = table.Column<short>(type: "smallint", nullable: false),
                    DropNotice = table.Column<short>(type: "smallint", nullable: false),
                    Droprate = table.Column<short>(type: "smallint", nullable: false),
                    Duration = table.Column<short>(type: "smallint", nullable: false),
                    Effect1 = table.Column<int>(type: "int", nullable: false),
                    Effect2 = table.Column<int>(type: "int", nullable: false),
                    Evasionrate = table.Column<short>(type: "smallint", nullable: false),
                    Extension = table.Column<int>(type: "int", nullable: false),
                    FireDamage = table.Column<short>(type: "smallint", nullable: false),
                    FireR = table.Column<short>(type: "smallint", nullable: false),
                    Grade = table.Column<short>(type: "smallint", nullable: false),
                    Hitrate = table.Column<short>(type: "smallint", nullable: false),
                    HpDrain = table.Column<short>(type: "smallint", nullable: false),
                    IceDamage = table.Column<short>(type: "smallint", nullable: false),
                    IntelB = table.Column<short>(type: "smallint", nullable: false),
                    ItemAlteration = table.Column<int>(type: "int", nullable: false),
                    ItemClass = table.Column<short>(type: "smallint", nullable: false),
                    ItemPlusId = table.Column<int>(type: "int", nullable: false),
                    ItemType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    JamadarAc = table.Column<short>(type: "smallint", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    LightningDamage = table.Column<short>(type: "smallint", nullable: false),
                    LightningR = table.Column<short>(type: "smallint", nullable: false),
                    MaceAc = table.Column<short>(type: "smallint", nullable: false),
                    MagicR = table.Column<short>(type: "smallint", nullable: false),
                    MaxDamage = table.Column<short>(type: "smallint", nullable: false),
                    MaxHpB = table.Column<short>(type: "smallint", nullable: false),
                    MaxMpB = table.Column<short>(type: "smallint", nullable: false),
                    MinDamage = table.Column<short>(type: "smallint", nullable: false),
                    MirrorDamage = table.Column<short>(type: "smallint", nullable: false),
                    MpDamage = table.Column<short>(type: "smallint", nullable: false),
                    MpDrain = table.Column<short>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NpBuyPrice = table.Column<int>(type: "int", nullable: false),
                    PoisonDamage = table.Column<short>(type: "smallint", nullable: false),
                    PoisonR = table.Column<short>(type: "smallint", nullable: false),
                    Race = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Range = table.Column<short>(type: "smallint", nullable: false),
                    ReqCha = table.Column<short>(type: "smallint", nullable: false),
                    ReqDex = table.Column<short>(type: "smallint", nullable: false),
                    ReqIntel = table.Column<short>(type: "smallint", nullable: false),
                    ReqLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReqLevelMax = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReqRank = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReqSta = table.Column<short>(type: "smallint", nullable: false),
                    ReqStr = table.Column<short>(type: "smallint", nullable: false),
                    ReqTitle = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SellNpcPrice = table.Column<int>(type: "int", nullable: false),
                    SellNpcType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SellPrice = table.Column<int>(type: "int", nullable: false),
                    SellingGroup = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Slot = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SpearAc = table.Column<short>(type: "smallint", nullable: false),
                    StaB = table.Column<short>(type: "smallint", nullable: false),
                    StrB = table.Column<short>(type: "smallint", nullable: false),
                    SwordAc = table.Column<short>(type: "smallint", nullable: false),
                    UpgradeNotice = table.Column<short>(type: "smallint", nullable: false),
                    Weight = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Num);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
