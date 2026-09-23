using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class LotteryEventData
{
    public const int DefaultDurationMinutes = 15;
    public const int DefaultUserLimit = 100;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = DefaultDurationMinutes;
    public int UserLimit { get; set; } = DefaultUserLimit;
    public int ReqItemId { get; set; } = InventoryConstants.ItemGold;
    public int ReqItemCount { get; set; } = 100000;
    public bool AutoStart { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<LotteryEventData>
    {
        public void Configure(EntityTypeBuilder<LotteryEventData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.Name).HasMaxLength(128);
        }
    }
}
