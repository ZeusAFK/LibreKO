using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class MarketPriceDay
{
    public int ItemId { get; set; }
    public DateOnly Day { get; set; }
    public int Trades { get; set; }
    public long Quantity { get; set; }
    public long Total { get; set; }
    public long MinPrice { get; set; }
    public long MaxPrice { get; set; }
    public DateTime LastTradeAt { get; set; }

    public long AveragePrice => Quantity > 0 ? Total / Quantity : 0;

    internal class EntityConfiguration : IEntityTypeConfiguration<MarketPriceDay>
    {
        public void Configure(EntityTypeBuilder<MarketPriceDay> builder)
        {
            builder.HasKey(p => new { p.ItemId, p.Day });
            builder.HasIndex(p => p.Day);
            builder.Ignore(p => p.AveragePrice);
        }
    }
}
