using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public enum AuctionBidStatus : byte
{
    Top = 1,
    Outbid = 2,
    Won = 3,
    Cancelled = 4,
}

public class AuctionBid
{
    public const int NameMax = 21;

    public int Id { get; set; }
    public int Channel { get; set; }
    public int Serial { get; set; }
    public byte Day { get; set; }
    public byte Slot { get; set; }
    public int ItemId { get; set; }
    public short Count { get; set; }
    public int CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public long Coins { get; set; }
    public int Checks { get; set; }
    public AuctionBidStatus Status { get; set; }
    public DateTime PlacedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public long Total(long checkValue) => Coins + Checks * checkValue;

    internal class EntityConfiguration : IEntityTypeConfiguration<AuctionBid>
    {
        public void Configure(EntityTypeBuilder<AuctionBid> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.CharacterName).HasMaxLength(NameMax);
            builder.HasIndex(p => new { p.Channel, p.Serial, p.Slot });
            builder.HasIndex(p => new { p.CharacterId, p.ClosedAt });
        }
    }
}
