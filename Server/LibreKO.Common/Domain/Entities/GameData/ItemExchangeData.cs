using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemExchangeData
{
    public int Index { get; set; }
    public short NpcId { get; set; }
    public byte RandomFlag { get; set; }

    // Origin items (up to 11, 1-indexed to match DB)
    public int OriginItem1 { get; set; }
    public int OriginCount1 { get; set; }
    public int OriginItem2 { get; set; }
    public int OriginCount2 { get; set; }
    public int OriginItem3 { get; set; }
    public int OriginCount3 { get; set; }
    public int OriginItem4 { get; set; }
    public int OriginCount4 { get; set; }
    public int OriginItem5 { get; set; }
    public int OriginCount5 { get; set; }
    public int OriginItem6 { get; set; }
    public int OriginCount6 { get; set; }
    public int OriginItem7 { get; set; }
    public int OriginCount7 { get; set; }
    public int OriginItem8 { get; set; }
    public int OriginCount8 { get; set; }
    public int OriginItem9 { get; set; }
    public int OriginCount9 { get; set; }
    public int OriginItem10 { get; set; }
    public int OriginCount10 { get; set; }
    public int OriginItem11 { get; set; }
    public int OriginCount11 { get; set; }

    // Exchange result items (up to 5, 1-indexed to match DB)
    public int ExchangeItem1 { get; set; }
    public int ExchangeCount1 { get; set; }
    public int ExchangeItem2 { get; set; }
    public int ExchangeCount2 { get; set; }
    public int ExchangeItem3 { get; set; }
    public int ExchangeCount3 { get; set; }
    public int ExchangeItem4 { get; set; }
    public int ExchangeCount4 { get; set; }
    public int ExchangeItem5 { get; set; }
    public int ExchangeCount5 { get; set; }

    public (int itemId, int count)[] GetOriginItems()
    {
        return
        [
            (OriginItem1, OriginCount1), (OriginItem2, OriginCount2), (OriginItem3, OriginCount3),
            (OriginItem4, OriginCount4), (OriginItem5, OriginCount5), (OriginItem6, OriginCount6),
            (OriginItem7, OriginCount7), (OriginItem8, OriginCount8), (OriginItem9, OriginCount9),
            (OriginItem10, OriginCount10), (OriginItem11, OriginCount11)
        ];
    }

    public (int itemId, int count)[] GetExchangeItems()
    {
        return
        [
            (ExchangeItem1, ExchangeCount1), (ExchangeItem2, ExchangeCount2),
            (ExchangeItem3, ExchangeCount3), (ExchangeItem4, ExchangeCount4),
            (ExchangeItem5, ExchangeCount5)
        ];
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemExchangeData>
    {
        public void Configure(EntityTypeBuilder<ItemExchangeData> builder)
        {
            builder.HasKey(p => p.Index);
            builder.Property(p => p.Index).ValueGeneratedNever();
        }
    }
}
