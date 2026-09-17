using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class Account : Entity
{
    public string Login { get; set; } = default!;
    public string Password { get; set; } = default!;
    public AccountNation Nation { get; set; } = AccountNation.None;
    public AccountAuthority Authority { get; set; } = AccountAuthority.Normal;
    public DateTime? PremiumDate { get; set; }

    public byte PremiumType { get; set; }
    public DateTime? AccessDate { get; set; }

    public int KnightCash { get; set; }

    public byte[] VipWarehouseItems { get; set; } = [];

    public DateTime VipVaultExpiry { get; set; }

    public string VipPassword { get; set; } = string.Empty;

    public string SealCode { get; set; } = string.Empty;

    public GameLanguage Language { get; set; } = GameLanguage.English;

    public int? OnlineServerId { get; set; }

    public DateTime? OnlineSince { get; set; }

    public byte ActivePremiumType => RemainingHoursUntil(PremiumDate) > 0 ? PremiumType : (byte)0;

    public short RemainingPremiumHours => RemainingHoursUntil(PremiumDate);

    public static short RemainingHoursUntil(DateTime? expiry)
    {
        if (expiry == null)
            return 0;

        var remaining = (expiry.Value - DateTime.UtcNow).TotalHours;
        return remaining <= 0 ? (short)0 : (short)Math.Min(remaining, short.MaxValue);
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<Account>
    {
        public void Configure(EntityTypeBuilder<Account> builder)
        {

            builder.HasKey(a => a.Id);

            builder.Property(a => a.Login).IsRequired().HasMaxLength(50);
            builder.Property(a => a.Password).IsRequired().HasMaxLength(255);
            builder.Property(a => a.Authority).IsRequired().HasConversion<string>();
            builder.Property(a => a.Nation).IsRequired().HasConversion<string>();
            builder.Property(a => a.AccessDate);
            builder.Property(a => a.PremiumDate);
            builder.Property(a => a.PremiumType);
            builder.Property(a => a.KnightCash);
            builder.Property(a => a.VipWarehouseItems);
            builder.Property(a => a.VipVaultExpiry);
            builder.Property(a => a.VipPassword).HasMaxLength(4);
            builder.Property(a => a.SealCode).HasMaxLength(8);
            builder.Property(a => a.Language).IsRequired().HasConversion<string>().HasMaxLength(16);
            builder.Property(a => a.OnlineServerId);
            builder.Property(a => a.OnlineSince);
        }
    }
}
