using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class Character : Entity
{
    public int AccountId { get; set; } = default!;
    public byte Slot { get; set; }
    public string Name { get; set; } = default!;

    public byte Race { get; set; } = default!;
    public short Class { get; set; }
    public int Hair { get; set; }
    public byte Face { get; set; }

    public int Money { get; set; }

    public int Hp { get; set; }
    public int Mp { get; set; }

    public byte Strength { get; set; }
    public byte Stamina { get; set; }
    public byte Dexterity { get; set; }
    public byte Intelligence { get; set; }
    public byte Magic { get; set; }

    public byte Level { get; set; }
    public short StatPoints { get; set; }
    public long Experience { get; set; }
    public long DeathExpLoss { get; set; }
    public int Loyalty { get; set; }
    public int LoyaltyMonthly { get; set; }
    public int LoyaltyDaily { get; set; }
    public short KnightsId { get; set; }
    public int KnightsPoints { get; set; }
    public byte Fame { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public byte MapId { get; set; }
    public bool IsOnline { get; set; }
    public byte[] Items { get; set; } = [];
    public byte[] SkillData { get; set; } = [];
    public byte[] SkillPointData { get; set; } = [];
    public short Bind { get; set; } = -1;
    public byte[] QuestData { get; set; } = [];
    public byte[] AchievementData { get; set; } = [];
    public short DisplayTitleId { get; set; }
    public byte[] SavedMagic { get; set; } = [];

    public DateTime? DeletionTime { get; set; }
    public DateTime? LastOnlineTime { get; set; }
    public int PlayMinutes { get; set; }
    public int MonstersDefeated { get; set; }
    public int PlayersDefeated { get; set; }
    public int Deaths { get; set; }

    public bool IsMuted { get; set; }

    // Pet companion (0 PetItemId = no pet). Satisfaction in 0..=10000.
    public int PetItemId { get; set; }
    public short PetSatisfaction { get; set; }
    public byte PetLevel { get; set; }
    public long PetExp { get; set; }

    // Rebirth (WIZ_REBIRTH 0xD3). RebirthLevel increments on each rebirth.
    // Reb* fields snapshot the stats at rebirth for bonus restoration.
    public short RebirthLevel { get; set; }
    public byte RebStr { get; set; }
    public byte RebSta { get; set; }
    public byte RebDex { get; set; }
    public byte RebIntel { get; set; }
    public byte RebMagic { get; set; }

    public DateTime? GenieExpiry { get; set; }

    public short GenieHours => RemainingGenieHours(GenieExpiry);

    public static short RemainingGenieHours(DateTime? expiry)
    {
        if (expiry == null)
            return 0;

        var remaining = (expiry.Value - DateTime.UtcNow).TotalHours;
        if (remaining <= 0)
            return 0;
        return remaining < 1 ? (short)1 : (short)Math.Min(Math.Round(remaining), short.MaxValue);
    }

    public byte[] GenieOptions { get; set; } = [];

    public ushort GenieMinutes => RemainingGenieMinutes(GenieExpiry);

    public static ushort RemainingGenieMinutes(DateTime? expiry)
    {
        if (expiry == null)
            return 0;

        var remaining = (expiry.Value - DateTime.UtcNow).TotalMinutes;
        if (remaining <= 0)
            return 0;
        return (ushort)Math.Min(Math.Ceiling(remaining), ushort.MaxValue);
    }

    public byte DrakiStage { get; set; }
    public byte DrakiSubStage { get; set; }

    public int AttendanceDays { get; set; }
    public int AttendanceClaimedDays { get; set; }
    public byte AttendanceClaimedBonus { get; set; }
    public DateTime? AttendanceCheckedOn { get; set; }

    public short GetPosX => (short)(X * 10);
    public short GetPosY => (short)(Y * 10);
    public short GetPosZ => (short)(Z * 10);

    internal class EntityConfiguration : IEntityTypeConfiguration<Character>
    {
        public void Configure(EntityTypeBuilder<Character> builder)
        {

            builder.HasKey(p => p.Id);

            builder.HasIndex(p => p.Name).IsUnique();
            builder.HasIndex(p => p.AccountId);

            builder.HasOne<Account>()
                .WithMany()
                .HasForeignKey(p => p.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Property(p => p.AccountId).IsRequired();
            builder.Property(p => p.Slot).IsRequired();
            builder.Property(p => p.Name).IsRequired().HasMaxLength(50);
            builder.Property(p => p.Race).IsRequired();
            builder.Property(p => p.Class).IsRequired();
            builder.Property(p => p.Hair).IsRequired();
            builder.Property(p => p.Face).IsRequired();
            builder.Property(p => p.Level).IsRequired();
            builder.Property(p => p.Experience).IsRequired();
            builder.Property(p => p.Money).IsRequired();
            builder.Property(p => p.Hp).IsRequired();
            builder.Property(p => p.Mp).IsRequired();
            builder.Property(p => p.Strength).IsRequired();
            builder.Property(p => p.Stamina).IsRequired();
            builder.Property(p => p.Dexterity).IsRequired();
            builder.Property(p => p.Intelligence).IsRequired();
            builder.Property(p => p.Magic).IsRequired();
            builder.Property(p => p.CreatedAt).IsRequired();
            builder.Property(p => p.DeletionTime);
            builder.Property(p => p.LastOnlineTime);
            builder.Property(p => p.PlayMinutes);
            builder.Property(p => p.MonstersDefeated);
            builder.Property(p => p.PlayersDefeated);
            builder.Property(p => p.Deaths);
            builder.Property(p => p.StatPoints);
            builder.Property(p => p.Loyalty);
            builder.Property(p => p.LoyaltyMonthly);
            builder.Property(p => p.LoyaltyDaily);
            builder.Property(p => p.KnightsId);
            builder.Property(p => p.KnightsPoints);
            builder.Property(p => p.Fame);
            builder.Property(p => p.IsOnline);
            builder.Property(p => p.Items);
            builder.Property(p => p.SkillData);
            builder.Property(p => p.SkillPointData);
            builder.Property(p => p.Bind);
            builder.Property(p => p.AttendanceDays);
            builder.Property(p => p.AttendanceClaimedDays);
            builder.Property(p => p.AttendanceClaimedBonus);
            builder.Property(p => p.AttendanceCheckedOn);
            builder.Property(p => p.QuestData);
            builder.Property(p => p.SavedMagic);
            builder.Property(p => p.MapId).IsRequired();
            builder.Property(p => p.X).IsRequired();
            builder.Property(p => p.Y).IsRequired();
            builder.Property(p => p.Z).IsRequired();
            builder.Property(p => p.IsMuted);
            builder.Property(p => p.PetItemId);
            builder.Property(p => p.PetSatisfaction);
            builder.Property(p => p.PetLevel);
            builder.Property(p => p.PetExp);
            builder.Property(p => p.RebirthLevel);
            builder.Property(p => p.RebStr);
            builder.Property(p => p.RebSta);
            builder.Property(p => p.RebDex);
            builder.Property(p => p.RebIntel);
            builder.Property(p => p.RebMagic);
        }
    }
}
