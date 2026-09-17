using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MagicType8Data
{
    public int Id { get; set; }
    public byte Target { get; set; }
    public short Radius { get; set; }
    public byte WarpType { get; set; }
    public short ExpRecover { get; set; }
    public short KickDistance { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MagicType8Data>
    {
        public void Configure(EntityTypeBuilder<MagicType8Data> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
