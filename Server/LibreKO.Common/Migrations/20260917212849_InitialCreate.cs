using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibreKO.Common.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Login = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Password = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nation = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Authority = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PremiumDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PremiumType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AccessDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    KnightCash = table.Column<int>(type: "int", nullable: false),
                    VipWarehouseItems = table.Column<byte[]>(type: "longblob", nullable: false),
                    VipVaultExpiry = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    VipPassword = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SealCode = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Language = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OnlineServerId = table.Column<int>(type: "int", nullable: true),
                    OnlineSince = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ConditionTable = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Tab = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Group = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Points = table.Column<short>(type: "smallint", nullable: false),
                    TitleId = table.Column<short>(type: "smallint", nullable: false),
                    RewardItemId = table.Column<int>(type: "int", nullable: false),
                    RewardItemCount = table.Column<short>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Target = table.Column<int>(type: "int", nullable: false),
                    Npcs = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Requires = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AchievementTitles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AchievementId = table.Column<int>(type: "int", nullable: false),
                    Strength = table.Column<short>(type: "smallint", nullable: false),
                    Hp = table.Column<short>(type: "smallint", nullable: false),
                    Dexterity = table.Column<short>(type: "smallint", nullable: false),
                    Intelligence = table.Column<short>(type: "smallint", nullable: false),
                    Magic = table.Column<short>(type: "smallint", nullable: false),
                    Attack = table.Column<short>(type: "smallint", nullable: false),
                    Defence = table.Column<short>(type: "smallint", nullable: false),
                    LoyaltyBonus = table.Column<short>(type: "smallint", nullable: false),
                    ExpBonus = table.Column<short>(type: "smallint", nullable: false),
                    ShortSwordAc = table.Column<short>(type: "smallint", nullable: false),
                    JamadarAc = table.Column<short>(type: "smallint", nullable: false),
                    SwordAc = table.Column<short>(type: "smallint", nullable: false),
                    BlowAc = table.Column<short>(type: "smallint", nullable: false),
                    AxeAc = table.Column<short>(type: "smallint", nullable: false),
                    SpearAc = table.Column<short>(type: "smallint", nullable: false),
                    ArrowAc = table.Column<short>(type: "smallint", nullable: false),
                    FireBonus = table.Column<short>(type: "smallint", nullable: false),
                    IceBonus = table.Column<short>(type: "smallint", nullable: false),
                    LightBonus = table.Column<short>(type: "smallint", nullable: false),
                    FireResist = table.Column<short>(type: "smallint", nullable: false),
                    IceResist = table.Column<short>(type: "smallint", nullable: false),
                    LightResist = table.Column<short>(type: "smallint", nullable: false),
                    MagicResist = table.Column<short>(type: "smallint", nullable: false),
                    CurseResist = table.Column<short>(type: "smallint", nullable: false),
                    PoisonResist = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementTitles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AttendanceRewards",
                columns: table => new
                {
                    Slot = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    ItemCount = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRewards", x => x.Slot);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Coefficients",
                columns: table => new
                {
                    ClassId = table.Column<short>(type: "smallint", nullable: false),
                    ShortSword = table.Column<double>(type: "double", nullable: false),
                    Jamadar = table.Column<double>(type: "double", nullable: false),
                    Sword = table.Column<double>(type: "double", nullable: false),
                    Axe = table.Column<double>(type: "double", nullable: false),
                    Club = table.Column<double>(type: "double", nullable: false),
                    Spear = table.Column<double>(type: "double", nullable: false),
                    Pole = table.Column<double>(type: "double", nullable: false),
                    Staff = table.Column<double>(type: "double", nullable: false),
                    Bow = table.Column<double>(type: "double", nullable: false),
                    Hp = table.Column<double>(type: "double", nullable: false),
                    Mp = table.Column<double>(type: "double", nullable: false),
                    Sp = table.Column<double>(type: "double", nullable: false),
                    Ac = table.Column<double>(type: "double", nullable: false),
                    Hitrate = table.Column<double>(type: "double", nullable: false),
                    Evasionrate = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coefficients", x => x.ClassId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventTriggers",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    NpcType = table.Column<short>(type: "smallint", nullable: false),
                    NpcId = table.Column<int>(type: "int", nullable: false),
                    TriggerNum = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTriggers", x => x.Index);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GameEvents",
                columns: table => new
                {
                    ZoneNum = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    EventNum = table.Column<short>(type: "smallint", nullable: false),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Cond1 = table.Column<int>(type: "int", nullable: false),
                    Cond2 = table.Column<int>(type: "int", nullable: false),
                    Cond3 = table.Column<int>(type: "int", nullable: false),
                    Cond4 = table.Column<int>(type: "int", nullable: false),
                    Cond5 = table.Column<int>(type: "int", nullable: false),
                    Exec1 = table.Column<int>(type: "int", nullable: false),
                    Exec2 = table.Column<int>(type: "int", nullable: false),
                    Exec3 = table.Column<int>(type: "int", nullable: false),
                    Exec4 = table.Column<int>(type: "int", nullable: false),
                    Exec5 = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameEvents", x => new { x.ZoneNum, x.EventNum });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Homes",
                columns: table => new
                {
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ElmoZoneX = table.Column<int>(type: "int", nullable: false),
                    ElmoZoneZ = table.Column<int>(type: "int", nullable: false),
                    ElmoZoneLX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ElmoZoneLZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    KarusZoneX = table.Column<int>(type: "int", nullable: false),
                    KarusZoneZ = table.Column<int>(type: "int", nullable: false),
                    KarusZoneLX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    KarusZoneLZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    FreeZoneX = table.Column<int>(type: "int", nullable: false),
                    FreeZoneZ = table.Column<int>(type: "int", nullable: false),
                    FreeZoneLX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    FreeZoneLZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZoneX = table.Column<int>(type: "int", nullable: false),
                    BattleZoneZ = table.Column<int>(type: "int", nullable: false),
                    BattleZoneLX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZoneLZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone2X = table.Column<int>(type: "int", nullable: false),
                    BattleZone2Z = table.Column<int>(type: "int", nullable: false),
                    BattleZone2LX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone2LZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone3X = table.Column<int>(type: "int", nullable: false),
                    BattleZone3Z = table.Column<int>(type: "int", nullable: false),
                    BattleZone3LX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone3LZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone4X = table.Column<int>(type: "int", nullable: false),
                    BattleZone4Z = table.Column<int>(type: "int", nullable: false),
                    BattleZone4LX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone4LZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone5X = table.Column<int>(type: "int", nullable: false),
                    BattleZone5Z = table.Column<int>(type: "int", nullable: false),
                    BattleZone5LX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone5LZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone6X = table.Column<int>(type: "int", nullable: false),
                    BattleZone6Z = table.Column<int>(type: "int", nullable: false),
                    BattleZone6LX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BattleZone6LZ = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Homes", x => x.Nation);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemExchanges",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    NpcId = table.Column<short>(type: "smallint", nullable: false),
                    RandomFlag = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    OriginItem1 = table.Column<int>(type: "int", nullable: false),
                    OriginCount1 = table.Column<int>(type: "int", nullable: false),
                    OriginItem2 = table.Column<int>(type: "int", nullable: false),
                    OriginCount2 = table.Column<int>(type: "int", nullable: false),
                    OriginItem3 = table.Column<int>(type: "int", nullable: false),
                    OriginCount3 = table.Column<int>(type: "int", nullable: false),
                    OriginItem4 = table.Column<int>(type: "int", nullable: false),
                    OriginCount4 = table.Column<int>(type: "int", nullable: false),
                    OriginItem5 = table.Column<int>(type: "int", nullable: false),
                    OriginCount5 = table.Column<int>(type: "int", nullable: false),
                    OriginItem6 = table.Column<int>(type: "int", nullable: false),
                    OriginCount6 = table.Column<int>(type: "int", nullable: false),
                    OriginItem7 = table.Column<int>(type: "int", nullable: false),
                    OriginCount7 = table.Column<int>(type: "int", nullable: false),
                    OriginItem8 = table.Column<int>(type: "int", nullable: false),
                    OriginCount8 = table.Column<int>(type: "int", nullable: false),
                    OriginItem9 = table.Column<int>(type: "int", nullable: false),
                    OriginCount9 = table.Column<int>(type: "int", nullable: false),
                    OriginItem10 = table.Column<int>(type: "int", nullable: false),
                    OriginCount10 = table.Column<int>(type: "int", nullable: false),
                    OriginItem11 = table.Column<int>(type: "int", nullable: false),
                    OriginCount11 = table.Column<int>(type: "int", nullable: false),
                    ExchangeItem1 = table.Column<int>(type: "int", nullable: false),
                    ExchangeCount1 = table.Column<int>(type: "int", nullable: false),
                    ExchangeItem2 = table.Column<int>(type: "int", nullable: false),
                    ExchangeCount2 = table.Column<int>(type: "int", nullable: false),
                    ExchangeItem3 = table.Column<int>(type: "int", nullable: false),
                    ExchangeCount3 = table.Column<int>(type: "int", nullable: false),
                    ExchangeItem4 = table.Column<int>(type: "int", nullable: false),
                    ExchangeCount4 = table.Column<int>(type: "int", nullable: false),
                    ExchangeItem5 = table.Column<int>(type: "int", nullable: false),
                    ExchangeCount5 = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemExchanges", x => x.Index);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemOps",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    TriggerType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SkillId = table.Column<int>(type: "int", nullable: false),
                    TriggerRate = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemOps", x => new { x.ItemId, x.TriggerType, x.SkillId });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Num = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Extension = table.Column<int>(type: "int", nullable: false),
                    ItemPlusId = table.Column<int>(type: "int", nullable: false),
                    ItemAlteration = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Slot = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Race = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Class = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Damage = table.Column<short>(type: "smallint", nullable: false),
                    MinDamage = table.Column<short>(type: "smallint", nullable: false),
                    MaxDamage = table.Column<short>(type: "smallint", nullable: false),
                    Delay = table.Column<short>(type: "smallint", nullable: false),
                    Range = table.Column<short>(type: "smallint", nullable: false),
                    Weight = table.Column<short>(type: "smallint", nullable: false),
                    Duration = table.Column<short>(type: "smallint", nullable: false),
                    BuyPrice = table.Column<int>(type: "int", nullable: false),
                    SellPrice = table.Column<int>(type: "int", nullable: false),
                    SellNpcType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SellNpcPrice = table.Column<int>(type: "int", nullable: false),
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
                    ItemType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Hitrate = table.Column<short>(type: "smallint", nullable: false),
                    Evasionrate = table.Column<short>(type: "smallint", nullable: false),
                    FireDamage = table.Column<short>(type: "smallint", nullable: false),
                    IceDamage = table.Column<short>(type: "smallint", nullable: false),
                    LightningDamage = table.Column<short>(type: "smallint", nullable: false),
                    PoisonDamage = table.Column<short>(type: "smallint", nullable: false),
                    HpDrain = table.Column<short>(type: "smallint", nullable: false),
                    MpDamage = table.Column<short>(type: "smallint", nullable: false),
                    MpDrain = table.Column<short>(type: "smallint", nullable: false),
                    MirrorDamage = table.Column<short>(type: "smallint", nullable: false),
                    Droprate = table.Column<short>(type: "smallint", nullable: false),
                    MaxHpB = table.Column<short>(type: "smallint", nullable: false),
                    MaxMpB = table.Column<short>(type: "smallint", nullable: false),
                    StrB = table.Column<short>(type: "smallint", nullable: false),
                    StaB = table.Column<short>(type: "smallint", nullable: false),
                    DexB = table.Column<short>(type: "smallint", nullable: false),
                    IntelB = table.Column<short>(type: "smallint", nullable: false),
                    ChaB = table.Column<short>(type: "smallint", nullable: false),
                    FireR = table.Column<short>(type: "smallint", nullable: false),
                    ColdR = table.Column<short>(type: "smallint", nullable: false),
                    LightningR = table.Column<short>(type: "smallint", nullable: false),
                    MagicR = table.Column<short>(type: "smallint", nullable: false),
                    PoisonR = table.Column<short>(type: "smallint", nullable: false),
                    CurseR = table.Column<short>(type: "smallint", nullable: false),
                    SellingGroup = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    DaggerAc = table.Column<short>(type: "smallint", nullable: false),
                    JamadarAc = table.Column<short>(type: "smallint", nullable: false),
                    SwordAc = table.Column<short>(type: "smallint", nullable: false),
                    AxeAc = table.Column<short>(type: "smallint", nullable: false),
                    MaceAc = table.Column<short>(type: "smallint", nullable: false),
                    SpearAc = table.Column<short>(type: "smallint", nullable: false),
                    BowAc = table.Column<short>(type: "smallint", nullable: false),
                    NpBuyPrice = table.Column<int>(type: "int", nullable: false),
                    Bound = table.Column<short>(type: "smallint", nullable: false),
                    Grade = table.Column<short>(type: "smallint", nullable: false),
                    DropNotice = table.Column<short>(type: "smallint", nullable: false),
                    UpgradeNotice = table.Column<short>(type: "smallint", nullable: false),
                    ItemClass = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Num);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemUpgradeRecipes",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OriginNumber = table.Column<int>(type: "int", nullable: false),
                    NewItemNote = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NewNumber = table.Column<int>(type: "int", nullable: false),
                    RequiredItem = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUpgradeRecipes", x => x.Index);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemUpgrades",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    NpcId = table.Column<short>(type: "smallint", nullable: false),
                    OriginType = table.Column<sbyte>(type: "tinyint", nullable: false),
                    OriginItem = table.Column<int>(type: "int", nullable: false),
                    ReqItem1 = table.Column<int>(type: "int", nullable: false),
                    ReqItem2 = table.Column<int>(type: "int", nullable: false),
                    ReqItem3 = table.Column<int>(type: "int", nullable: false),
                    ReqItem4 = table.Column<int>(type: "int", nullable: false),
                    ReqItem5 = table.Column<int>(type: "int", nullable: false),
                    ReqItem6 = table.Column<int>(type: "int", nullable: false),
                    ReqItem7 = table.Column<int>(type: "int", nullable: false),
                    ReqItem8 = table.Column<int>(type: "int", nullable: false),
                    ReqNoah = table.Column<int>(type: "int", nullable: false),
                    RateType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    GenRate = table.Column<short>(type: "smallint", nullable: false),
                    GiveItem = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUpgrades", x => x.Index);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemUpgradeSettings",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    ReqItem1 = table.Column<int>(type: "int", nullable: false),
                    ReqItem2 = table.Column<int>(type: "int", nullable: false),
                    ItemType = table.Column<short>(type: "smallint", nullable: false),
                    ItemRate = table.Column<short>(type: "smallint", nullable: false),
                    ItemGrade = table.Column<short>(type: "smallint", nullable: false),
                    ReqNoah = table.Column<int>(type: "int", nullable: false),
                    SuccessRate = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUpgradeSettings", x => x.Index);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KingBallotBoxes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AccountId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CharId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    CandidacyId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KingBallotBoxes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KingCandidacyNoticeBoards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NoticeLen = table.Column<short>(type: "smallint", nullable: false),
                    Notice = table.Column<byte[]>(type: "longblob", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KingCandidacyNoticeBoards", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KingElectionLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Knights = table.Column<short>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Money = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KingElectionLists", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KingSystem",
                columns: table => new
                {
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Year = table.Column<short>(type: "smallint", nullable: false),
                    Month = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Day = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Hour = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Minute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ImType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ImYear = table.Column<short>(type: "smallint", nullable: false),
                    ImMonth = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ImDay = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ImHour = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ImMinute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NoahEvent = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NoahEventDay = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NoahEventHour = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NoahEventMinute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NoahEventDuration = table.Column<short>(type: "smallint", nullable: false),
                    ExpEvent = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExpEventDay = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExpEventHour = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExpEventMinute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExpEventDuration = table.Column<short>(type: "smallint", nullable: false),
                    Tribute = table.Column<int>(type: "int", nullable: false),
                    TerritoryTariff = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    TerritoryTax = table.Column<int>(type: "int", nullable: false),
                    NationalTreasury = table.Column<int>(type: "int", nullable: false),
                    KingName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notice = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImRequestId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KingSystem", x => x.Nation);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Knights",
                columns: table => new
                {
                    IDNum = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    IDName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Chief = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Flag = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Ranking = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Members = table.Column<short>(type: "smallint", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    ClanPointFund = table.Column<int>(type: "int", nullable: false),
                    sCape = table.Column<short>(type: "smallint", nullable: false),
                    bCapeR = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    bCapeG = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    bCapeB = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Notice = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClanWarehouseItems = table.Column<byte[]>(type: "longblob", nullable: false),
                    ClanWarehouseGold = table.Column<int>(type: "int", nullable: false),
                    PremiumExpiry = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    MarkVersion = table.Column<short>(type: "smallint", nullable: false),
                    MarkData = table.Column<byte[]>(type: "longblob", nullable: false),
                    AllianceId = table.Column<short>(type: "smallint", nullable: false),
                    AllianceReq = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Knights", x => x.IDNum);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KnightsAlliances",
                columns: table => new
                {
                    sMainAllianceKnights = table.Column<short>(type: "smallint", nullable: false),
                    sSubAllianceKnights = table.Column<short>(type: "smallint", nullable: false),
                    sMercenaryClan_1 = table.Column<short>(type: "smallint", nullable: false),
                    sMercenaryClan_2 = table.Column<short>(type: "smallint", nullable: false),
                    Notice = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnightsAlliances", x => x.sMainAllianceKnights);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KnightsCapes",
                columns: table => new
                {
                    CapeIndex = table.Column<short>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BuyPrice = table.Column<int>(type: "int", nullable: false),
                    Duration = table.Column<int>(type: "int", nullable: false),
                    Grade = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    BuyLoyalty = table.Column<int>(type: "int", nullable: false),
                    Ranking = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnightsCapes", x => x.CapeIndex);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LevelUp",
                columns: table => new
                {
                    Level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Exp = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelUp", x => x.Level);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Magic",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    EnName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    KrName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    BeforeAction = table.Column<int>(type: "int", nullable: false),
                    TargetAction = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SelfEffect = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    FlyingEffect = table.Column<short>(type: "smallint", nullable: false),
                    TargetEffect = table.Column<short>(type: "smallint", nullable: false),
                    Moral = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SkillLevel = table.Column<short>(type: "smallint", nullable: false),
                    Skill = table.Column<short>(type: "smallint", nullable: false),
                    Msp = table.Column<short>(type: "smallint", nullable: false),
                    Hp = table.Column<short>(type: "smallint", nullable: false),
                    Sp = table.Column<short>(type: "smallint", nullable: false),
                    ItemGroup = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    UseItem = table.Column<int>(type: "int", nullable: false),
                    CastTime = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReCastTime = table.Column<short>(type: "smallint", nullable: false),
                    SuccessRate = table.Column<short>(type: "smallint", nullable: false),
                    Type1 = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Type2 = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Range = table.Column<short>(type: "smallint", nullable: false),
                    Etc = table.Column<short>(type: "smallint", nullable: false),
                    UseStanding = table.Column<short>(type: "smallint", nullable: false),
                    SkillCheck = table.Column<short>(type: "smallint", nullable: false),
                    IceLightRate = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Magic", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType1",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    HitType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    HitRate = table.Column<short>(type: "smallint", nullable: false),
                    Hit = table.Column<short>(type: "smallint", nullable: false),
                    AddDamage = table.Column<short>(type: "smallint", nullable: false),
                    Delay = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ComboType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ComboCount = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ComboDamage = table.Column<short>(type: "smallint", nullable: false),
                    Range = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType1", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType2",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    HitType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    HitRate = table.Column<short>(type: "smallint", nullable: false),
                    AddDamage = table.Column<short>(type: "smallint", nullable: false),
                    AddRange = table.Column<short>(type: "smallint", nullable: false),
                    NeedArrow = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType2", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType3",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DirectType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    FirstDamage = table.Column<short>(type: "smallint", nullable: false),
                    TimeDamage = table.Column<short>(type: "smallint", nullable: false),
                    Duration = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Attribute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Radius = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType3", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType4",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    BuffType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Radius = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Duration = table.Column<short>(type: "smallint", nullable: false),
                    AttackSpeed = table.Column<short>(type: "smallint", nullable: false),
                    Speed = table.Column<short>(type: "smallint", nullable: false),
                    Ac = table.Column<short>(type: "smallint", nullable: false),
                    AcPct = table.Column<short>(type: "smallint", nullable: false),
                    Attack = table.Column<short>(type: "smallint", nullable: false),
                    MagicAttack = table.Column<short>(type: "smallint", nullable: false),
                    MaxHP = table.Column<int>(type: "int", nullable: false),
                    MaxHPPct = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MaxMP = table.Column<int>(type: "int", nullable: false),
                    MaxMPPct = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    HitRate = table.Column<short>(type: "smallint", nullable: false),
                    AvoidRate = table.Column<short>(type: "smallint", nullable: false),
                    Str = table.Column<short>(type: "smallint", nullable: false),
                    Sta = table.Column<short>(type: "smallint", nullable: false),
                    Dex = table.Column<short>(type: "smallint", nullable: false),
                    Intel = table.Column<short>(type: "smallint", nullable: false),
                    Cha = table.Column<short>(type: "smallint", nullable: false),
                    FireR = table.Column<short>(type: "smallint", nullable: false),
                    ColdR = table.Column<short>(type: "smallint", nullable: false),
                    LightningR = table.Column<short>(type: "smallint", nullable: false),
                    MagicR = table.Column<short>(type: "smallint", nullable: false),
                    DiseaseR = table.Column<short>(type: "smallint", nullable: false),
                    PoisonR = table.Column<short>(type: "smallint", nullable: false),
                    ExpPct = table.Column<short>(type: "smallint", nullable: false),
                    SpecialAmount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType4", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType5",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExpRecover = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NeedStone = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType5", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType6",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Size = table.Column<short>(type: "smallint", nullable: false),
                    TransformId = table.Column<short>(type: "smallint", nullable: false),
                    Duration = table.Column<short>(type: "smallint", nullable: false),
                    MaxHp = table.Column<short>(type: "smallint", nullable: false),
                    MaxMp = table.Column<short>(type: "smallint", nullable: false),
                    Speed = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AttackSpeed = table.Column<short>(type: "smallint", nullable: false),
                    TotalHit = table.Column<short>(type: "smallint", nullable: false),
                    TotalAc = table.Column<short>(type: "smallint", nullable: false),
                    TotalHitRate = table.Column<short>(type: "smallint", nullable: false),
                    TotalEvasionRate = table.Column<short>(type: "smallint", nullable: false),
                    TotalFireR = table.Column<short>(type: "smallint", nullable: false),
                    TotalColdR = table.Column<short>(type: "smallint", nullable: false),
                    TotalLightningR = table.Column<short>(type: "smallint", nullable: false),
                    TotalMagicR = table.Column<short>(type: "smallint", nullable: false),
                    TotalDiseaseR = table.Column<short>(type: "smallint", nullable: false),
                    TotalPoisonR = table.Column<short>(type: "smallint", nullable: false),
                    Class = table.Column<short>(type: "smallint", nullable: false),
                    UserSkillUse = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NeedItem = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SkillSuccessRate = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MonsterFriendly = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Nation = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType6", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType7",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ValidGroup = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NationChange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MonsterNum = table.Column<short>(type: "smallint", nullable: false),
                    TargetChange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    StateChange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Radius = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    HitRate = table.Column<short>(type: "smallint", nullable: false),
                    Duration = table.Column<short>(type: "smallint", nullable: false),
                    Damage = table.Column<short>(type: "smallint", nullable: false),
                    Vision = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NeedItem = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType7", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType8",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Target = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Radius = table.Column<short>(type: "smallint", nullable: false),
                    WarpType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExpRecover = table.Column<short>(type: "smallint", nullable: false),
                    KickDistance = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType8", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MagicType9",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ValidGroup = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    NationChange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MonsterNum = table.Column<short>(type: "smallint", nullable: false),
                    TargetChange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    StateChange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Radius = table.Column<short>(type: "smallint", nullable: false),
                    HitRate = table.Column<short>(type: "smallint", nullable: false),
                    Duration = table.Column<short>(type: "smallint", nullable: false),
                    AddDamage = table.Column<short>(type: "smallint", nullable: false),
                    Vision = table.Column<short>(type: "smallint", nullable: false),
                    NeedItem = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MagicType9", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MailBoxes",
                columns: table => new
                {
                    nLetterID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    dtSendDate = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    dtReadDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    bStatus = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    strSenderID = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strRecipientID = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strSubject = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strMessage = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    bType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    nItemID = table.Column<int>(type: "int", nullable: false),
                    sCount = table.Column<short>(type: "smallint", nullable: false),
                    sDurability = table.Column<short>(type: "smallint", nullable: false),
                    nSerialNum = table.Column<long>(type: "bigint", nullable: false),
                    nCoins = table.Column<int>(type: "int", nullable: false),
                    bDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MailBoxes", x => x.nLetterID);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MakeItemGroups",
                columns: table => new
                {
                    GroupNum = table.Column<int>(type: "int", nullable: false),
                    Items = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MakeItemGroups", x => x.GroupNum);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MonsterSummons",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    Sid = table.Column<short>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Level = table.Column<short>(type: "smallint", nullable: false),
                    Probability = table.Column<short>(type: "smallint", nullable: false),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonsterSummons", x => x.Index);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "NpcItems",
                columns: table => new
                {
                    Index = table.Column<short>(type: "smallint", nullable: false),
                    IsMonster = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Item1 = table.Column<int>(type: "int", nullable: false),
                    Percent1 = table.Column<short>(type: "smallint", nullable: false),
                    Item2 = table.Column<int>(type: "int", nullable: false),
                    Percent2 = table.Column<short>(type: "smallint", nullable: false),
                    Item3 = table.Column<int>(type: "int", nullable: false),
                    Percent3 = table.Column<short>(type: "smallint", nullable: false),
                    Item4 = table.Column<int>(type: "int", nullable: false),
                    Percent4 = table.Column<short>(type: "smallint", nullable: false),
                    Item5 = table.Column<int>(type: "int", nullable: false),
                    Percent5 = table.Column<short>(type: "smallint", nullable: false),
                    Item6 = table.Column<int>(type: "int", nullable: false),
                    Percent6 = table.Column<short>(type: "smallint", nullable: false),
                    Item7 = table.Column<int>(type: "int", nullable: false),
                    Percent7 = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpcItems", x => new { x.Index, x.IsMonster });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "NpcPositions",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    ZoneId = table.Column<short>(type: "smallint", nullable: false),
                    NpcId = table.Column<int>(type: "int", nullable: false),
                    ActType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    DotCnt = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Path = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LeftX = table.Column<int>(type: "int", nullable: false),
                    TopZ = table.Column<int>(type: "int", nullable: false),
                    NumNPC = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RegTime = table.Column<short>(type: "smallint", nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    SpawnRange = table.Column<short>(type: "smallint", nullable: false),
                    RegenType = table.Column<short>(type: "smallint", nullable: false),
                    DungeonFamily = table.Column<short>(type: "smallint", nullable: false),
                    SpecialType = table.Column<short>(type: "smallint", nullable: false),
                    TrapNumber = table.Column<short>(type: "smallint", nullable: false),
                    Room = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NpcPositions", x => x.Index);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Npcs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    IsMonster = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NpcType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    IsBoss = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Group = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Rank = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Title = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Level = table.Column<short>(type: "smallint", nullable: false),
                    Hp = table.Column<int>(type: "int", nullable: false),
                    Mp = table.Column<short>(type: "smallint", nullable: false),
                    Ac = table.Column<short>(type: "smallint", nullable: false),
                    Attack1 = table.Column<short>(type: "smallint", nullable: false),
                    Attack2 = table.Column<short>(type: "smallint", nullable: false),
                    Money = table.Column<int>(type: "int", nullable: false),
                    Experience = table.Column<int>(type: "int", nullable: false),
                    Loyalty = table.Column<int>(type: "int", nullable: false),
                    ModelId = table.Column<short>(type: "smallint", nullable: false),
                    Size = table.Column<short>(type: "smallint", nullable: false),
                    WeaponType1 = table.Column<int>(type: "int", nullable: false),
                    WeaponType2 = table.Column<int>(type: "int", nullable: false),
                    HitRate = table.Column<short>(type: "smallint", nullable: false),
                    EvadeRate = table.Column<short>(type: "smallint", nullable: false),
                    FireR = table.Column<short>(type: "smallint", nullable: false),
                    ColdR = table.Column<short>(type: "smallint", nullable: false),
                    LightningR = table.Column<short>(type: "smallint", nullable: false),
                    MagicR = table.Column<short>(type: "smallint", nullable: false),
                    PoisonR = table.Column<short>(type: "smallint", nullable: false),
                    CurseR = table.Column<short>(type: "smallint", nullable: false),
                    SellingGroup = table.Column<int>(type: "int", nullable: false),
                    ItemGroup = table.Column<short>(type: "smallint", nullable: false),
                    ActType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AttackDelay = table.Column<short>(type: "smallint", nullable: false),
                    Speed = table.Column<short>(type: "smallint", nullable: false),
                    Speed1 = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Speed2 = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Standtime = table.Column<short>(type: "smallint", nullable: false),
                    AttackRange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    SearchRange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    TracingRange = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Bulk = table.Column<short>(type: "smallint", nullable: false),
                    DirectAttack = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MagicAttack = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Magic1 = table.Column<int>(type: "int", nullable: false),
                    Magic2 = table.Column<int>(type: "int", nullable: false),
                    Magic3 = table.Column<int>(type: "int", nullable: false),
                    Family = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AreaRange = table.Column<double>(type: "double", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Npcs", x => new { x.Id, x.IsMonster });
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Patches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FileId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FileSize = table.Column<int>(type: "int", nullable: false),
                    FileChecksum = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patches", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PremiumItemExps",
                columns: table => new
                {
                    Index = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MinLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    MaxLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Percent = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PremiumItemExps", x => x.Index);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PremiumItems",
                columns: table => new
                {
                    Type = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ExpRestorePercent = table.Column<double>(type: "double", nullable: false),
                    NoahPercent = table.Column<short>(type: "smallint", nullable: false),
                    DropPercent = table.Column<short>(type: "smallint", nullable: false),
                    BonusLoyalty = table.Column<int>(type: "int", nullable: false),
                    RepairDiscountPercent = table.Column<short>(type: "smallint", nullable: false),
                    ItemSellPercent = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PremiumItems", x => x.Type);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SeedStates",
                columns: table => new
                {
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RowCount = table.Column<long>(type: "bigint", nullable: false),
                    AppliedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedStates", x => x.Name);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ServerGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerGroups", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ServerResources",
                columns: table => new
                {
                    ResourceId = table.Column<int>(type: "int", nullable: false),
                    Resource = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerResources", x => x.ResourceId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SetItems",
                columns: table => new
                {
                    SetIndex = table.Column<int>(type: "int", nullable: false),
                    HPBonus = table.Column<short>(type: "smallint", nullable: false),
                    MPBonus = table.Column<short>(type: "smallint", nullable: false),
                    StrengthBonus = table.Column<short>(type: "smallint", nullable: false),
                    StaminaBonus = table.Column<short>(type: "smallint", nullable: false),
                    DexterityBonus = table.Column<short>(type: "smallint", nullable: false),
                    IntelBonus = table.Column<short>(type: "smallint", nullable: false),
                    CharismaBonus = table.Column<short>(type: "smallint", nullable: false),
                    FlameResistance = table.Column<short>(type: "smallint", nullable: false),
                    GlacierResistance = table.Column<short>(type: "smallint", nullable: false),
                    LightningResistance = table.Column<short>(type: "smallint", nullable: false),
                    PoisonResistance = table.Column<short>(type: "smallint", nullable: false),
                    MagicResistance = table.Column<short>(type: "smallint", nullable: false),
                    CurseResistance = table.Column<short>(type: "smallint", nullable: false),
                    XPBonusPercent = table.Column<short>(type: "smallint", nullable: false),
                    CoinBonusPercent = table.Column<short>(type: "smallint", nullable: false),
                    APBonusPercent = table.Column<short>(type: "smallint", nullable: false),
                    APBonusClassType = table.Column<short>(type: "smallint", nullable: false),
                    APBonusClassPercent = table.Column<short>(type: "smallint", nullable: false),
                    ACBonus = table.Column<short>(type: "smallint", nullable: false),
                    ACBonusClassType = table.Column<short>(type: "smallint", nullable: false),
                    ACBonusClassPercent = table.Column<short>(type: "smallint", nullable: false),
                    MaxWeightBonus = table.Column<short>(type: "smallint", nullable: false),
                    NPBonus = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetItems", x => x.SetIndex);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SheriffReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ReporterCharId = table.Column<int>(type: "int", nullable: false),
                    TargetCharId = table.Column<int>(type: "int", nullable: false),
                    TargetName = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VoteYesCount = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    VoteNoCount = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    CreatedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SheriffReports", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SheriffVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ReportId = table.Column<int>(type: "int", nullable: false),
                    VoterCharId = table.Column<int>(type: "int", nullable: false),
                    VoteYes = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SheriffVotes", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SiegeWarfare",
                columns: table => new
                {
                    CastleIndex = table.Column<short>(type: "smallint", nullable: false),
                    MasterKnights = table.Column<short>(type: "smallint", nullable: false),
                    SiegeType = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    WarDay = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    WarTime = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    WarMinute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ChallengeList1 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList2 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList3 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList4 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList5 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList6 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList7 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList8 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList9 = table.Column<short>(type: "smallint", nullable: false),
                    ChallengeList10 = table.Column<short>(type: "smallint", nullable: false),
                    WarRequestDay = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    WarRequestTime = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    WarRequestMinute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    GuerrillaWarDay = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    GuerrillaWarTime = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    GuerrillaWarMinute = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ChallengeListStr = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MoradonTariff = table.Column<short>(type: "smallint", nullable: false),
                    DellosTariff = table.Column<short>(type: "smallint", nullable: false),
                    DungeonCharge = table.Column<int>(type: "int", nullable: false),
                    MoradonTax = table.Column<int>(type: "int", nullable: false),
                    DellosTax = table.Column<int>(type: "int", nullable: false),
                    RequestList1 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList2 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList3 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList4 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList5 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList6 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList7 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList8 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList9 = table.Column<short>(type: "smallint", nullable: false),
                    RequestList10 = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiegeWarfare", x => x.CastleIndex);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "StartPositions",
                columns: table => new
                {
                    ZoneId = table.Column<short>(type: "smallint", nullable: false),
                    KarusX = table.Column<short>(type: "smallint", nullable: false),
                    KarusZ = table.Column<short>(type: "smallint", nullable: false),
                    ElmoradX = table.Column<short>(type: "smallint", nullable: false),
                    ElmoradZ = table.Column<short>(type: "smallint", nullable: false),
                    KarusGateX = table.Column<short>(type: "smallint", nullable: false),
                    KarusGateZ = table.Column<short>(type: "smallint", nullable: false),
                    ElmoGateX = table.Column<short>(type: "smallint", nullable: false),
                    ElmoGateZ = table.Column<short>(type: "smallint", nullable: false),
                    RangeX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RangeZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    KarusRangeX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    KarusRangeZ = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ElmoradRangeX = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ElmoradRangeZ = table.Column<byte>(type: "tinyint unsigned", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StartPositions", x => x.ZoneId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserDailyOps",
                columns: table => new
                {
                    CharacterId = table.Column<int>(type: "int", nullable: false),
                    ChaosMapTime = table.Column<int>(type: "int", nullable: false),
                    UserRankRewardTime = table.Column<int>(type: "int", nullable: false),
                    PersonalRankRewardTime = table.Column<int>(type: "int", nullable: false),
                    KingWingTime = table.Column<int>(type: "int", nullable: false),
                    WarderKillerTime1 = table.Column<int>(type: "int", nullable: false),
                    WarderKillerTime2 = table.Column<int>(type: "int", nullable: false),
                    KeeperKillerTime = table.Column<int>(type: "int", nullable: false),
                    UserLoyaltyWingRewardTime = table.Column<int>(type: "int", nullable: false),
                    LadderRewardTime = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDailyOps", x => x.CharacterId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Warps",
                columns: table => new
                {
                    WarpId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fee = table.Column<int>(type: "int", nullable: false),
                    ZoneId = table.Column<short>(type: "smallint", nullable: false),
                    X = table.Column<short>(type: "smallint", nullable: false),
                    Z = table.Column<short>(type: "smallint", nullable: false),
                    Y = table.Column<short>(type: "smallint", nullable: false),
                    GroupId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warps", x => x.WarpId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ZoneInfos",
                columns: table => new
                {
                    ZoneNo = table.Column<short>(type: "smallint", nullable: false),
                    ServerNo = table.Column<short>(type: "smallint", nullable: false),
                    SmdName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MapName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InitX = table.Column<int>(type: "int", nullable: false),
                    InitZ = table.Column<int>(type: "int", nullable: false),
                    InitY = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZoneInfos", x => x.ZoneNo);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Characters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Slot = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Race = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Class = table.Column<short>(type: "smallint", nullable: false),
                    Hair = table.Column<int>(type: "int", nullable: false),
                    Face = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Money = table.Column<int>(type: "int", nullable: false),
                    Hp = table.Column<int>(type: "int", nullable: false),
                    Mp = table.Column<int>(type: "int", nullable: false),
                    Strength = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Stamina = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Dexterity = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Intelligence = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Magic = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Level = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    StatPoints = table.Column<short>(type: "smallint", nullable: false),
                    Experience = table.Column<long>(type: "bigint", nullable: false),
                    DeathExpLoss = table.Column<long>(type: "bigint", nullable: false),
                    Loyalty = table.Column<int>(type: "int", nullable: false),
                    LoyaltyMonthly = table.Column<int>(type: "int", nullable: false),
                    LoyaltyDaily = table.Column<int>(type: "int", nullable: false),
                    KnightsId = table.Column<short>(type: "smallint", nullable: false),
                    KnightsPoints = table.Column<int>(type: "int", nullable: false),
                    Fame = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    X = table.Column<float>(type: "float", nullable: false),
                    Y = table.Column<float>(type: "float", nullable: false),
                    Z = table.Column<float>(type: "float", nullable: false),
                    MapId = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    IsOnline = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Items = table.Column<byte[]>(type: "longblob", nullable: false),
                    SkillData = table.Column<byte[]>(type: "longblob", nullable: false),
                    SkillPointData = table.Column<byte[]>(type: "longblob", nullable: false),
                    Bind = table.Column<short>(type: "smallint", nullable: false),
                    QuestData = table.Column<byte[]>(type: "longblob", nullable: false),
                    AchievementData = table.Column<byte[]>(type: "longblob", nullable: false),
                    DisplayTitleId = table.Column<short>(type: "smallint", nullable: false),
                    SavedMagic = table.Column<byte[]>(type: "longblob", nullable: false),
                    DeletionTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastOnlineTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PlayMinutes = table.Column<int>(type: "int", nullable: false),
                    MonstersDefeated = table.Column<int>(type: "int", nullable: false),
                    PlayersDefeated = table.Column<int>(type: "int", nullable: false),
                    Deaths = table.Column<int>(type: "int", nullable: false),
                    IsMuted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PetItemId = table.Column<int>(type: "int", nullable: false),
                    PetSatisfaction = table.Column<short>(type: "smallint", nullable: false),
                    PetLevel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    PetExp = table.Column<long>(type: "bigint", nullable: false),
                    RebirthLevel = table.Column<short>(type: "smallint", nullable: false),
                    RebStr = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RebSta = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RebDex = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RebIntel = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    RebMagic = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    GenieExpiry = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GenieOptions = table.Column<byte[]>(type: "longblob", nullable: false),
                    DrakiStage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    DrakiSubStage = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AttendanceDays = table.Column<int>(type: "int", nullable: false),
                    AttendanceClaimedDays = table.Column<int>(type: "int", nullable: false),
                    AttendanceClaimedBonus = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AttendanceCheckedOn = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Characters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Characters_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    Items = table.Column<byte[]>(type: "longblob", nullable: false),
                    Money = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Warehouses_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    Category = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    IpAddress = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LanIpAddress = table.Column<string>(type: "varchar(15)", maxLength: 15, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Port = table.Column<int>(type: "int", nullable: false),
                    OnlinePlayers = table.Column<int>(type: "int", nullable: false),
                    MaxPlayers = table.Column<int>(type: "int", nullable: false),
                    FreePlayerCap = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAt = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Servers_ServerGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ServerGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Friendships",
                columns: table => new
                {
                    CharacterId = table.Column<int>(type: "int", nullable: false),
                    FriendCharacterId = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendships", x => new { x.CharacterId, x.FriendCharacterId });
                    table.ForeignKey(
                        name: "FK_Friendships_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Friendships_Characters_FriendCharacterId",
                        column: x => x.FriendCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_AccountId",
                table: "Characters",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Characters_Name",
                table: "Characters",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_CharacterId",
                table: "Friendships",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_FriendCharacterId",
                table: "Friendships",
                column: "FriendCharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemUpgradeSettings_ItemType_ItemGrade",
                table: "ItemUpgradeSettings",
                columns: new[] { "ItemType", "ItemGrade" });

            migrationBuilder.CreateIndex(
                name: "IX_Servers_GroupId",
                table: "Servers",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SheriffReports_Status",
                table: "SheriffReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SheriffReports_TargetCharId",
                table: "SheriffReports",
                column: "TargetCharId");

            migrationBuilder.CreateIndex(
                name: "IX_SheriffVotes_ReportId_VoterCharId",
                table: "SheriffVotes",
                columns: new[] { "ReportId", "VoterCharId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_AccountId",
                table: "Warehouses",
                column: "AccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Achievements");

            migrationBuilder.DropTable(
                name: "AchievementTitles");

            migrationBuilder.DropTable(
                name: "AttendanceRewards");

            migrationBuilder.DropTable(
                name: "Coefficients");

            migrationBuilder.DropTable(
                name: "EventTriggers");

            migrationBuilder.DropTable(
                name: "Friendships");

            migrationBuilder.DropTable(
                name: "GameEvents");

            migrationBuilder.DropTable(
                name: "Homes");

            migrationBuilder.DropTable(
                name: "ItemExchanges");

            migrationBuilder.DropTable(
                name: "ItemOps");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "ItemUpgradeRecipes");

            migrationBuilder.DropTable(
                name: "ItemUpgrades");

            migrationBuilder.DropTable(
                name: "ItemUpgradeSettings");

            migrationBuilder.DropTable(
                name: "KingBallotBoxes");

            migrationBuilder.DropTable(
                name: "KingCandidacyNoticeBoards");

            migrationBuilder.DropTable(
                name: "KingElectionLists");

            migrationBuilder.DropTable(
                name: "KingSystem");

            migrationBuilder.DropTable(
                name: "Knights");

            migrationBuilder.DropTable(
                name: "KnightsAlliances");

            migrationBuilder.DropTable(
                name: "KnightsCapes");

            migrationBuilder.DropTable(
                name: "LevelUp");

            migrationBuilder.DropTable(
                name: "Magic");

            migrationBuilder.DropTable(
                name: "MagicType1");

            migrationBuilder.DropTable(
                name: "MagicType2");

            migrationBuilder.DropTable(
                name: "MagicType3");

            migrationBuilder.DropTable(
                name: "MagicType4");

            migrationBuilder.DropTable(
                name: "MagicType5");

            migrationBuilder.DropTable(
                name: "MagicType6");

            migrationBuilder.DropTable(
                name: "MagicType7");

            migrationBuilder.DropTable(
                name: "MagicType8");

            migrationBuilder.DropTable(
                name: "MagicType9");

            migrationBuilder.DropTable(
                name: "MailBoxes");

            migrationBuilder.DropTable(
                name: "MakeItemGroups");

            migrationBuilder.DropTable(
                name: "MonsterSummons");

            migrationBuilder.DropTable(
                name: "NpcItems");

            migrationBuilder.DropTable(
                name: "NpcPositions");

            migrationBuilder.DropTable(
                name: "Npcs");

            migrationBuilder.DropTable(
                name: "Patches");

            migrationBuilder.DropTable(
                name: "PremiumItemExps");

            migrationBuilder.DropTable(
                name: "PremiumItems");

            migrationBuilder.DropTable(
                name: "SeedStates");

            migrationBuilder.DropTable(
                name: "ServerResources");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "SetItems");

            migrationBuilder.DropTable(
                name: "SheriffReports");

            migrationBuilder.DropTable(
                name: "SheriffVotes");

            migrationBuilder.DropTable(
                name: "SiegeWarfare");

            migrationBuilder.DropTable(
                name: "StartPositions");

            migrationBuilder.DropTable(
                name: "UserDailyOps");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "Warps");

            migrationBuilder.DropTable(
                name: "ZoneInfos");

            migrationBuilder.DropTable(
                name: "Characters");

            migrationBuilder.DropTable(
                name: "ServerGroups");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
