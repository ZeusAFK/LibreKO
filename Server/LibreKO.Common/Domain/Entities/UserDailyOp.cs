using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class UserDailyOp
{
    public const int Count = (int)DailyOperation.SpiritGuardianBlack + 1;

    public int CharacterId { get; set; }
    public int ChaosMapTime { get; set; }
    public int UserRankRewardTime { get; set; }
    public int PersonalRankRewardTime { get; set; }
    public int KingWingTime { get; set; }
    public int WarderKillerTime1 { get; set; }
    public int WarderKillerTime2 { get; set; }
    public int KeeperKillerTime { get; set; }
    public int UserLoyaltyWingRewardTime { get; set; }
    public int LadderRewardTime { get; set; }
    public int SpiritGuardianRedTime { get; set; }
    public int SpiritGuardianBlueTime { get; set; }
    public int SpiritGuardianBlackTime { get; set; }

    public int[] ToTimestamps() =>
    [
        0,
        ChaosMapTime,
        UserRankRewardTime,
        PersonalRankRewardTime,
        KingWingTime,
        WarderKillerTime1,
        WarderKillerTime2,
        KeeperKillerTime,
        UserLoyaltyWingRewardTime,
        LadderRewardTime,
        SpiritGuardianRedTime,
        SpiritGuardianBlueTime,
        SpiritGuardianBlackTime,
    ];

    public void FromTimestamps(int[] timestamps)
    {
        if (timestamps.Length < Count)
            return;

        ChaosMapTime = timestamps[(int)DailyOperation.ChaosMap];
        UserRankRewardTime = timestamps[(int)DailyOperation.NationRankReward];
        PersonalRankRewardTime = timestamps[(int)DailyOperation.PersonalRankReward];
        KingWingTime = timestamps[(int)DailyOperation.KingWing];
        WarderKillerTime1 = timestamps[(int)DailyOperation.WarderKillerWing1];
        WarderKillerTime2 = timestamps[(int)DailyOperation.WarderKillerWing2];
        KeeperKillerTime = timestamps[(int)DailyOperation.KeeperKillerWing];
        UserLoyaltyWingRewardTime = timestamps[(int)DailyOperation.LoyaltyWingReward];
        LadderRewardTime = timestamps[(int)DailyOperation.LadderReward];
        SpiritGuardianRedTime = timestamps[(int)DailyOperation.SpiritGuardianRed];
        SpiritGuardianBlueTime = timestamps[(int)DailyOperation.SpiritGuardianBlue];
        SpiritGuardianBlackTime = timestamps[(int)DailyOperation.SpiritGuardianBlack];
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<UserDailyOp>
    {
        public void Configure(EntityTypeBuilder<UserDailyOp> builder)
        {
            builder.HasKey(p => p.CharacterId);
            builder.Property(p => p.CharacterId).ValueGeneratedNever();
        }
    }
}
