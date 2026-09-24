using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemData
{
    public const byte TypeKrowaz = 4;
    public const byte TypeStandard = 5;
    public const byte TypeRebirth = 11;
    public const byte TypeRebirthKrowaz = 12;
    public const int Effect2ExchangePiece = 251;

    public int Num { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Extension { get; set; }
    public int ItemPlusId { get; set; }
    public int ItemAlteration { get; set; }
    public byte Kind { get; set; }
    public byte Slot { get; set; }
    public byte Race { get; set; }
    public bool IsUntradeable => Race == UntradeableRace;
    private const byte UntradeableRace = 20;
    public byte Class { get; set; }
    public short Damage { get; set; }
    public short MinDamage { get; set; }
    public short MaxDamage { get; set; }
    public short Delay { get; set; }
    public short Range { get; set; }
    public short Weight { get; set; }
    public short Duration { get; set; }
    public int BuyPrice { get; set; }
    public int SellPrice { get; set; }
    public byte SellNpcType { get; set; }
    public int SellNpcPrice { get; set; }
    public short Ac { get; set; }
    public byte Countable { get; set; }
    public int Effect1 { get; set; }
    public int Effect2 { get; set; }
    public byte ReqLevel { get; set; }
    public byte ReqLevelMax { get; set; }
    public byte ReqRank { get; set; }
    public byte ReqTitle { get; set; }
    public short ReqStr { get; set; }
    public short ReqSta { get; set; }
    public short ReqDex { get; set; }
    public short ReqIntel { get; set; }
    public short ReqCha { get; set; }
    public byte ItemType { get; set; }
    public short Hitrate { get; set; }
    public short Evasionrate { get; set; }
    public short FireDamage { get; set; }
    public short IceDamage { get; set; }
    public short LightningDamage { get; set; }
    public short PoisonDamage { get; set; }
    public short HpDrain { get; set; }
    public short MpDamage { get; set; }
    public short MpDrain { get; set; }
    public short MirrorDamage { get; set; }
    public short Droprate { get; set; }
    public short MaxHpB { get; set; }
    public short MaxMpB { get; set; }
    public short StrB { get; set; }
    public short StaB { get; set; }
    public short DexB { get; set; }
    public short IntelB { get; set; }
    public short ChaB { get; set; }
    public short FireR { get; set; }
    public short ColdR { get; set; }
    public short LightningR { get; set; }
    public short MagicR { get; set; }
    public short PoisonR { get; set; }
    public short CurseR { get; set; }
    public byte SellingGroup { get; set; }

    // Weapon-type AC resistances (reduce incoming damage from specific weapon types)
    public short DaggerAc { get; set; }
    public short JamadarAc { get; set; }
    public short SwordAc { get; set; }
    public short AxeAc { get; set; }
    public short MaceAc { get; set; }
    public short SpearAc { get; set; }
    public short BowAc { get; set; }
    public int NpBuyPrice { get; set; }
    public short Bound { get; set; }
    public short Grade { get; set; }
    public short DropNotice { get; set; }
    public short UpgradeNotice { get; set; }
    public short ItemClass { get; set; }

    public ItemKind Category => (ItemKind)Kind;

    public bool IsBow() => Category
        is ItemKind.Bow or ItemKind.Crossbow or ItemKind.LongBow or ItemKind.Launcher;

    public bool IsShield() => Category == ItemKind.Shield;

    public bool IsChargeItem => Category == ItemKind.PowerUpStore && Countable == 0;

    public int CarriedUnits(ushort count) => IsChargeItem ? Math.Min((int)count, 1) : count;

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemData>
    {
        public void Configure(EntityTypeBuilder<ItemData> builder)
        {
            builder.HasKey(p => p.Num);
            builder.Property(p => p.Num).ValueGeneratedNever();
        }
    }
}
