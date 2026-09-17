using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ZoneInfoData
{
    public short ServerNo { get; set; }
    public short ZoneNo { get; set; }
    public string SmdName { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public int InitX { get; set; }
    public int InitZ { get; set; }
    public int InitY { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<ZoneInfoData>
    {
        public void Configure(EntityTypeBuilder<ZoneInfoData> builder)
        {
            builder.HasKey(p => p.ZoneNo);
            builder.Property(p => p.ZoneNo).ValueGeneratedNever();
        }
    }
}
