using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class KnightsAllianceEntity
{
    public short MainClanId { get; set; }
    public short SubClanId { get; set; }
    public short MercenaryClan1 { get; set; }
    public short MercenaryClan2 { get; set; }
    public string Notice { get; set; } = string.Empty;

    public IEnumerable<short> GetAllClanIds()
    {
        if (MainClanId > 0) yield return MainClanId;
        if (SubClanId > 0) yield return SubClanId;
        if (MercenaryClan1 > 0) yield return MercenaryClan1;
        if (MercenaryClan2 > 0) yield return MercenaryClan2;
    }

    public bool IsFull => SubClanId > 0 && MercenaryClan1 > 0 && MercenaryClan2 > 0;
    public bool IsEmpty => SubClanId == 0 && MercenaryClan1 == 0 && MercenaryClan2 == 0;

    public bool RemoveMember(short clanId)
    {
        if (SubClanId == clanId) { SubClanId = 0; return true; }
        if (MercenaryClan1 == clanId) { MercenaryClan1 = 0; return true; }
        if (MercenaryClan2 == clanId) { MercenaryClan2 = 0; return true; }
        return false;
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<KnightsAllianceEntity>
    {
        public void Configure(EntityTypeBuilder<KnightsAllianceEntity> builder)
        {
            builder.HasKey(k => k.MainClanId);

            builder.Property(k => k.MainClanId).HasColumnName("sMainAllianceKnights").ValueGeneratedNever();
            builder.Property(k => k.SubClanId).HasColumnName("sSubAllianceKnights");
            builder.Property(k => k.MercenaryClan1).HasColumnName("sMercenaryClan_1");
            builder.Property(k => k.MercenaryClan2).HasColumnName("sMercenaryClan_2");
            builder.Property(k => k.Notice).HasColumnName("Notice").HasMaxLength(255);
        }
    }
}
