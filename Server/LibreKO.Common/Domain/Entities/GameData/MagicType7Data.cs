using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicType7Data
{
    public int Id { get; set; }
    public byte ValidGroup { get; set; }
    public byte NationChange { get; set; }
    public short MonsterNum { get; set; }
    public byte TargetChange { get; set; }
    public byte StateChange { get; set; }
    public byte Radius { get; set; }
    public short HitRate { get; set; }
    public short Duration { get; set; }
    public short Damage { get; set; }
    public byte Vision { get; set; }
    public int NeedItem { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicType7Data>
    {
        public void Configure(EntityTypeBuilder<MagicType7Data> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
