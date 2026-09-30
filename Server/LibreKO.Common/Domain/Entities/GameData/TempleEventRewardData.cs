using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class TempleEventRewardData
{
    public int Id { get; set; }
    public TempleEvent Event { get; set; }
    public TempleEventRewardOutcome Outcome { get; set; }
    public int ItemId { get; set; }
    public int ItemCount { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public int LoyaltyPoints { get; set; }
    public int ExpPercent { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<TempleEventRewardData>
    {
        public void Configure(EntityTypeBuilder<TempleEventRewardData> builder)
        {
            builder.ToTable("TempleEventRewards");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => new { p.Event, p.Outcome });
        }
    }
}
