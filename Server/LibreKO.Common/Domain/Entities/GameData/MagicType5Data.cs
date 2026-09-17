using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicType5Data
{
    public int Id { get; set; }
    public byte Type { get; set; }
    public byte ExpRecover { get; set; }
    public short NeedStone { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicType5Data>
    {
        public void Configure(EntityTypeBuilder<MagicType5Data> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
