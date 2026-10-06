using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class SpecialAuctionLotData
{
    public int Row { get; set; }
    public byte Slot { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    public long StartPrice { get; set; }
    public int Step { get; set; }
    public bool Secret { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<SpecialAuctionLotData>
    {
        public void Configure(EntityTypeBuilder<SpecialAuctionLotData> builder)
        {
            builder.HasKey(p => new { p.Row, p.Slot });
        }
    }
}
