using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations.Schema;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicData
{
    public int Id { get; set; }
    public string EnName { get; set; } = string.Empty;
    public string KrName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Tier { get; set; }
    public int BeforeAction { get; set; }
    public byte TargetAction { get; set; }
    public byte SelfEffect { get; set; }
    public short FlyingEffect { get; set; }
    public short TargetEffect { get; set; }
    public byte Moral { get; set; }
    public short SkillLevel { get; set; }
    public short Skill { get; set; }
    public short Msp { get; set; }
    public short Hp { get; set; }
    public short Sp { get; set; }
    public byte ItemGroup { get; set; }
    public int UseItem { get; set; }
    public byte CastTime { get; set; }
    public short ReCastTime { get; set; }
    public short SuccessRate { get; set; }
    public byte Type1 { get; set; }
    public byte Type2 { get; set; }

    [NotMapped]
    public MagicSkillType PrimaryType => (MagicSkillType)Type1;

    [NotMapped]
    public MagicSkillType SecondaryType => (MagicSkillType)Type2;

    private const int MillisecondsPerTenth = 100;

    [NotMapped]
    public int CastTimeMs => CastTime * MillisecondsPerTenth;
    public short Range { get; set; }
    public short Etc { get; set; }
    public short UseStanding { get; set; }
    public short SkillCheck { get; set; }
    public short IceLightRate { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicData>
    {
        public void Configure(EntityTypeBuilder<MagicData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
