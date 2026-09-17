using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class SetItemData
{
    public const short NoJobGroup = 255;
    public const short FirstJobGroup = 1;
    public const short LastJobGroup = 4;

    public static bool IsJobGroup(short classType) =>
        classType is >= FirstJobGroup and <= LastJobGroup;

    public int SetIndex { get; set; }
    public short HPBonus { get; set; }
    public short MPBonus { get; set; }
    public short StrengthBonus { get; set; }
    public short StaminaBonus { get; set; }
    public short DexterityBonus { get; set; }
    public short IntelBonus { get; set; }
    public short CharismaBonus { get; set; }
    public short FlameResistance { get; set; }
    public short GlacierResistance { get; set; }
    public short LightningResistance { get; set; }
    public short PoisonResistance { get; set; }
    public short MagicResistance { get; set; }
    public short CurseResistance { get; set; }
    public short XPBonusPercent { get; set; }
    public short CoinBonusPercent { get; set; }
    public short APBonusPercent { get; set; }
    public short APBonusClassType { get; set; }
    public short APBonusClassPercent { get; set; }
    public short ACBonus { get; set; }
    public short ACBonusClassType { get; set; }
    public short ACBonusClassPercent { get; set; }
    public short MaxWeightBonus { get; set; }
    public byte NPBonus { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<SetItemData>
    {
        public void Configure(EntityTypeBuilder<SetItemData> builder)
        {
            builder.HasKey(p => p.SetIndex);
            builder.Property(p => p.SetIndex).ValueGeneratedNever();
        }
    }
}
