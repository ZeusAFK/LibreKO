using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemUpgradeData
{
    public int Index { get; set; }
    public short NpcId { get; set; }
    public sbyte OriginType { get; set; }
    public int OriginItem { get; set; }
    public int ReqItem1 { get; set; }
    public int ReqItem2 { get; set; }
    public int ReqItem3 { get; set; }
    public int ReqItem4 { get; set; }
    public int ReqItem5 { get; set; }
    public int ReqItem6 { get; set; }
    public int ReqItem7 { get; set; }
    public int ReqItem8 { get; set; }
    public int ReqNoah { get; set; }
    public byte RateType { get; set; }
    public short GenRate { get; set; }
    public int GiveItem { get; set; }

    public int[] GetRequiredItems() =>
    [
        ReqItem1, ReqItem2, ReqItem3, ReqItem4,
        ReqItem5, ReqItem6, ReqItem7, ReqItem8
    ];

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemUpgradeData>
    {
        public void Configure(EntityTypeBuilder<ItemUpgradeData> builder)
        {
            builder.HasKey(p => p.Index);
            builder.Property(p => p.Index).ValueGeneratedNever();
        }
    }
}
