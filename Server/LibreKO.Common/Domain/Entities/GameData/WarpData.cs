using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class WarpData
{
    public int WarpId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Fee { get; set; }
    public short ZoneId { get; set; }
    public short X { get; set; }
    public short Z { get; set; }
    public short Y { get; set; }
    public int GroupId { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<WarpData>
    {
        public void Configure(EntityTypeBuilder<WarpData> builder)
        {
            builder.HasKey(p => p.WarpId);
            builder.Property(p => p.WarpId).ValueGeneratedNever();
        }
    }
}
