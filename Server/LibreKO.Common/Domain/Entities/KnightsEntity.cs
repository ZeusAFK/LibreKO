using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class KnightsEntity
{
    public short Id { get; set; }
    public string Name { get; set; } = default!;
    public string Chief { get; set; } = default!;
    public byte Nation { get; set; }
    public byte Flag { get; set; } // ClanType: 1=training, 2=promoted, etc.
    public byte Grade { get; set; } // Ranking
    public short Members { get; set; }
    public int Points { get; set; }
    public int ClanPointFund { get; set; }
    public short Cape { get; set; }
    public byte CapeR { get; set; }
    public byte CapeG { get; set; }
    public byte CapeB { get; set; }

    public string Notice { get; set; } = string.Empty;

    public byte[] ClanWarehouseItems { get; set; } = [];

    public int ClanWarehouseGold { get; set; }

    public DateTime? PremiumExpiry { get; set; }

    public bool HasPremium => PremiumExpiry > DateTime.UtcNow;

    public short MarkVersion { get; set; }

    public byte[] MarkData { get; set; } = [];

    public short AllianceId { get; set; }

    public short AllianceReq { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<KnightsEntity>
    {
        public void Configure(EntityTypeBuilder<KnightsEntity> builder)
        {

            builder.HasKey(k => k.Id);

            builder.Property(k => k.Id).HasColumnName("IDNum");
            builder.Property(k => k.Name).HasColumnName("IDName").IsRequired().HasMaxLength(50);
            builder.Property(k => k.Chief).HasColumnName("Chief").IsRequired().HasMaxLength(50);
            builder.Property(k => k.Nation).HasColumnName("Nation").IsRequired();
            builder.Property(k => k.Flag).HasColumnName("Flag").IsRequired();
            builder.Property(k => k.Grade).HasColumnName("Ranking");
            builder.Property(k => k.Members).HasColumnName("Members");
            builder.Property(k => k.Points).HasColumnName("Points");
            builder.Property(k => k.Cape).HasColumnName("sCape");
            builder.Property(k => k.CapeR).HasColumnName("bCapeR");
            builder.Property(k => k.CapeG).HasColumnName("bCapeG");
            builder.Property(k => k.CapeB).HasColumnName("bCapeB");

            builder.Property(k => k.ClanPointFund).HasColumnName("ClanPointFund");
            builder.Property(k => k.Notice).HasColumnName("Notice").HasMaxLength(255);
            builder.Property(k => k.ClanWarehouseItems).HasColumnName("ClanWarehouseItems");
            builder.Property(k => k.ClanWarehouseGold).HasColumnName("ClanWarehouseGold");
            builder.Property(k => k.MarkVersion).HasColumnName("MarkVersion");
            builder.Property(k => k.MarkData).HasColumnName("MarkData");
            builder.Property(k => k.AllianceId).HasColumnName("AllianceId");
            builder.Property(k => k.AllianceReq).HasColumnName("AllianceReq");
        }
    }
}
