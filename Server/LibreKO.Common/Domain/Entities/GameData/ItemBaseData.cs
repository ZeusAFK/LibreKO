using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemBaseData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte VariantSet { get; set; }
    public byte Kind { get; set; }
    public byte Slot { get; set; }
    public byte Race { get; set; }
    public byte Class { get; set; }
    public short Damage { get; set; }
    public short Delay { get; set; }
    public short Range { get; set; }
    public short Weight { get; set; }
    public short Duration { get; set; }
    public int BuyPrice { get; set; }
    public int SaleType { get; set; }
    public short Ac { get; set; }
    public byte Countable { get; set; }
    public int Effect1 { get; set; }
    public int Effect2 { get; set; }
    public byte ReqLevel { get; set; }
    public byte ReqLevelMax { get; set; }
    public byte ReqRank { get; set; }
    public byte ReqTitle { get; set; }
    public short ReqStr { get; set; }
    public short ReqSta { get; set; }
    public short ReqDex { get; set; }
    public short ReqIntel { get; set; }
    public short ReqCha { get; set; }
    public byte SellingGroup { get; set; }
    public short ItemClass { get; set; }
    public short UnbindStones { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemBaseData>
    {
        public void Configure(EntityTypeBuilder<ItemBaseData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
