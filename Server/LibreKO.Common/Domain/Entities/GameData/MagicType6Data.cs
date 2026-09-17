using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicType6Data
{
    public int Id { get; set; }
    public short Size { get; set; }
    public short TransformId { get; set; }
    public short Duration { get; set; }
    public short MaxHp { get; set; }
    public short MaxMp { get; set; }
    public byte Speed { get; set; }
    public short AttackSpeed { get; set; }
    public short TotalHit { get; set; }
    public short TotalAc { get; set; }
    public short TotalHitRate { get; set; }
    public short TotalEvasionRate { get; set; }
    public short TotalFireR { get; set; }
    public short TotalColdR { get; set; }
    public short TotalLightningR { get; set; }
    public short TotalMagicR { get; set; }
    public short TotalDiseaseR { get; set; }
    public short TotalPoisonR { get; set; }
    public short Class { get; set; }
    public byte UserSkillUse { get; set; }
    public byte NeedItem { get; set; }
    public byte SkillSuccessRate { get; set; }
    public byte MonsterFriendly { get; set; }
    public byte Nation { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicType6Data>
    {
        public void Configure(EntityTypeBuilder<MagicType6Data> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
