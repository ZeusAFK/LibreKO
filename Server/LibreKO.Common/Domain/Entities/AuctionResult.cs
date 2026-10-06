using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class AuctionResult
{
    public int Id { get; set; }
    public int Channel { get; set; }
    public int Serial { get; set; }
    public byte Slot { get; set; }
    public int ItemId { get; set; }
    public long Price { get; set; }
    public AuctionBidStatus Status { get; set; }
    public DateTime SettledAt { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<AuctionResult>
    {
        public void Configure(EntityTypeBuilder<AuctionResult> builder)
        {
            builder.HasKey(p => p.Id);
            builder.HasIndex(p => new { p.Channel, p.Serial });
        }
    }
}
