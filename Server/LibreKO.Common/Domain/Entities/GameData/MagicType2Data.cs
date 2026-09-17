using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicType2Data
{
    public int Id { get; set; }
    public byte HitType { get; set; }
    public short HitRate { get; set; }
    public short AddDamage { get; set; }
    public short AddRange { get; set; }
    public byte NeedArrow { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicType2Data>
    {
        public void Configure(EntityTypeBuilder<MagicType2Data> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
