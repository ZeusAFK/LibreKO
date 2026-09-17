using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicType1Data
{
    public int Id { get; set; }
    public byte HitType { get; set; }
    public short HitRate { get; set; }
    public short Hit { get; set; }
    public short AddDamage { get; set; }
    public byte Delay { get; set; }
    public byte ComboType { get; set; }
    public byte ComboCount { get; set; }
    public short ComboDamage { get; set; }
    public short Range { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicType1Data>
    {
        public void Configure(EntityTypeBuilder<MagicType1Data> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
