using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class AttendanceRewardData
{
    public const int DailySlotFirst = 1;
    public const int DailySlotLast = 25;
    public const int BonusSlotFirst = 101;
    public const int BonusSlotCount = 3;
    public const int BonusFirstThreshold = 15;
    public const int BonusThresholdStep = 5;

    public int Slot { get; set; }
    public int ItemId { get; set; }
    public short ItemCount { get; set; }

    public static bool IsDailySlot(int slot) => slot is >= DailySlotFirst and <= DailySlotLast;

    public static bool IsBonusSlot(int slot) =>
        slot >= BonusSlotFirst && slot < BonusSlotFirst + BonusSlotCount;

    public static int BonusThreshold(int slot) =>
        BonusFirstThreshold + (slot - BonusSlotFirst) * BonusThresholdStep;

    internal class EntityConfiguration : IEntityTypeConfiguration<AttendanceRewardData>
    {
        public void Configure(EntityTypeBuilder<AttendanceRewardData> builder)
        {
            builder.HasKey(p => p.Slot);
            builder.Property(p => p.Slot).ValueGeneratedNever();
        }
    }
}
