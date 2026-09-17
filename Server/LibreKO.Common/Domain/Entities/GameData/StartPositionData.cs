using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class StartPositionData
{
    public short ZoneId { get; set; }
    public short KarusX { get; set; }
    public short KarusZ { get; set; }
    public short ElmoradX { get; set; }
    public short ElmoradZ { get; set; }
    public short KarusGateX { get; set; }
    public short KarusGateZ { get; set; }
    public short ElmoGateX { get; set; }
    public short ElmoGateZ { get; set; }
    public byte RangeX { get; set; }
    public byte RangeZ { get; set; }
    public byte KarusRangeX { get; set; }
    public byte KarusRangeZ { get; set; }
    public byte ElmoradRangeX { get; set; }
    public byte ElmoradRangeZ { get; set; }

    public short BaseX(AccountNation nation) => nation == AccountNation.Karus ? KarusX : ElmoradX;

    public short BaseZ(AccountNation nation) => nation == AccountNation.Karus ? KarusZ : ElmoradZ;

    public (short X, short Z) RandomSpawn(AccountNation nation) =>
        ((short)(BaseX(nation) + Random.Shared.Next(0, RangeXFor(nation) + 1)),
         (short)(BaseZ(nation) + Random.Shared.Next(0, RangeZFor(nation) + 1)));

    private byte RangeXFor(AccountNation nation) => nation == AccountNation.Karus
        ? (KarusRangeX != 0 ? KarusRangeX : RangeX)
        : (ElmoradRangeX != 0 ? ElmoradRangeX : RangeX);

    private byte RangeZFor(AccountNation nation) => nation == AccountNation.Karus
        ? (KarusRangeZ != 0 ? KarusRangeZ : RangeZ)
        : (ElmoradRangeZ != 0 ? ElmoradRangeZ : RangeZ);

    internal class EntityConfiguration : IEntityTypeConfiguration<StartPositionData>
    {
        public void Configure(EntityTypeBuilder<StartPositionData> builder)
        {
            builder.HasKey(p => p.ZoneId);
            builder.Property(p => p.ZoneId).ValueGeneratedNever();
        }
    }
}
