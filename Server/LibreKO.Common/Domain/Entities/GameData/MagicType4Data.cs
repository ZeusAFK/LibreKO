using LibreKO.Common.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json.Serialization;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicType4Data
{
    public int Id { get; set; }
    public byte BuffType { get; set; }
    public byte Radius { get; set; }
    public short Duration { get; set; }
    public short AttackSpeed { get; set; }
    public short Speed { get; set; }
    public short Ac { get; set; }
    public short AcPct { get; set; }
    public short Attack { get; set; }
    public short MagicAttack { get; set; }
    public int MaxHP { get; set; }
    public byte MaxHPPct { get; set; }
    public int MaxMP { get; set; }
    public byte MaxMPPct { get; set; }
    public short HitRate { get; set; }
    [JsonConverter(typeof(NullToDefaultInt16JsonConverter))]
    public short AvoidRate { get; set; }
    public short Str { get; set; }
    public short Sta { get; set; }
    public short Dex { get; set; }
    public short Intel { get; set; }
    public short Cha { get; set; }
    public short FireR { get; set; }
    public short ColdR { get; set; }
    public short LightningR { get; set; }
    public short MagicR { get; set; }
    public short DiseaseR { get; set; }
    public short PoisonR { get; set; }
    public short ExpPct { get; set; }
    public int SpecialAmount { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicType4Data>
    {
        public void Configure(EntityTypeBuilder<MagicType4Data> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
