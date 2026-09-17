using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicType3Data
{
    public int Id { get; set; }
    public byte DirectType { get; set; }
    public short FirstDamage { get; set; }
    public short TimeDamage { get; set; }
    public byte Duration { get; set; }
    public byte Attribute { get; set; }
    public byte Radius { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicType3Data>
    {
        public void Configure(EntityTypeBuilder<MagicType3Data> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
