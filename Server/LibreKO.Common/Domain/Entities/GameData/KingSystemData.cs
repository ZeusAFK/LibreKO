using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class KingSystemData
{
    public byte Nation { get; set; }
    public byte Type { get; set; }
    public short Year { get; set; }
    public byte Month { get; set; }
    public byte Day { get; set; }
    public byte Hour { get; set; }
    public byte Minute { get; set; }
    public byte ImType { get; set; }
    public short ImYear { get; set; }
    public byte ImMonth { get; set; }
    public byte ImDay { get; set; }
    public byte ImHour { get; set; }
    public byte ImMinute { get; set; }
    public byte NoahEvent { get; set; }
    public byte NoahEventDay { get; set; }
    public byte NoahEventHour { get; set; }
    public byte NoahEventMinute { get; set; }
    public short NoahEventDuration { get; set; }
    public byte ExpEvent { get; set; }
    public byte ExpEventDay { get; set; }
    public byte ExpEventHour { get; set; }
    public byte ExpEventMinute { get; set; }
    public short ExpEventDuration { get; set; }
    public int Tribute { get; set; }
    public byte TerritoryTariff { get; set; }
    public int TerritoryTax { get; set; }
    public int NationalTreasury { get; set; }
    public string KingName { get; set; } = string.Empty;
    public string Notice { get; set; } = string.Empty;
    public string ImRequestId { get; set; } = string.Empty;

    internal class EntityConfiguration : IEntityTypeConfiguration<KingSystemData>
    {
        public void Configure(EntityTypeBuilder<KingSystemData> builder)
        {
            builder.HasKey(p => p.Nation);
            builder.Property(p => p.Nation).ValueGeneratedNever();
        }
    }
}
