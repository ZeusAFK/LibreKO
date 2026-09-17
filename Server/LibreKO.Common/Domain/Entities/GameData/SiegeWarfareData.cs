using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class SiegeWarfareData
{
    public short CastleIndex { get; set; }
    public short MasterKnights { get; set; }
    public byte SiegeType { get; set; }
    public byte WarDay { get; set; }
    public byte WarTime { get; set; }
    public byte WarMinute { get; set; }
    public short ChallengeList1 { get; set; }
    public short ChallengeList2 { get; set; }
    public short ChallengeList3 { get; set; }
    public short ChallengeList4 { get; set; }
    public short ChallengeList5 { get; set; }
    public short ChallengeList6 { get; set; }
    public short ChallengeList7 { get; set; }
    public short ChallengeList8 { get; set; }
    public short ChallengeList9 { get; set; }
    public short ChallengeList10 { get; set; }
    public byte WarRequestDay { get; set; }
    public byte WarRequestTime { get; set; }
    public byte WarRequestMinute { get; set; }
    public byte GuerrillaWarDay { get; set; }
    public byte GuerrillaWarTime { get; set; }
    public byte GuerrillaWarMinute { get; set; }
    public string ChallengeListStr { get; set; } = string.Empty;
    public short MoradonTariff { get; set; }
    public short DellosTariff { get; set; }
    public int DungeonCharge { get; set; }
    public int MoradonTax { get; set; }
    public int DellosTax { get; set; }
    public short RequestList1 { get; set; }
    public short RequestList2 { get; set; }
    public short RequestList3 { get; set; }
    public short RequestList4 { get; set; }
    public short RequestList5 { get; set; }
    public short RequestList6 { get; set; }
    public short RequestList7 { get; set; }
    public short RequestList8 { get; set; }
    public short RequestList9 { get; set; }
    public short RequestList10 { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<SiegeWarfareData>
    {
        public void Configure(EntityTypeBuilder<SiegeWarfareData> builder)
        {
            builder.HasKey(p => p.CastleIndex);
            builder.Property(p => p.CastleIndex).ValueGeneratedNever();
        }
    }
}
