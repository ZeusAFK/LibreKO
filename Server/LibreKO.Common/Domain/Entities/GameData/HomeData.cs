using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class HomeData
{
    public byte Nation { get; set; }

    public int ElmoZoneX { get; set; }
    public int ElmoZoneZ { get; set; }
    public byte ElmoZoneLX { get; set; }
    public byte ElmoZoneLZ { get; set; }

    public int KarusZoneX { get; set; }
    public int KarusZoneZ { get; set; }
    public byte KarusZoneLX { get; set; }
    public byte KarusZoneLZ { get; set; }

    public int FreeZoneX { get; set; }
    public int FreeZoneZ { get; set; }
    public byte FreeZoneLX { get; set; }
    public byte FreeZoneLZ { get; set; }

    public int BattleZoneX { get; set; }
    public int BattleZoneZ { get; set; }
    public byte BattleZoneLX { get; set; }
    public byte BattleZoneLZ { get; set; }

    public int BattleZone2X { get; set; }
    public int BattleZone2Z { get; set; }
    public byte BattleZone2LX { get; set; }
    public byte BattleZone2LZ { get; set; }

    public int BattleZone3X { get; set; }
    public int BattleZone3Z { get; set; }
    public byte BattleZone3LX { get; set; }
    public byte BattleZone3LZ { get; set; }

    public int BattleZone4X { get; set; }
    public int BattleZone4Z { get; set; }
    public byte BattleZone4LX { get; set; }
    public byte BattleZone4LZ { get; set; }

    public int BattleZone5X { get; set; }
    public int BattleZone5Z { get; set; }
    public byte BattleZone5LX { get; set; }
    public byte BattleZone5LZ { get; set; }

    public int BattleZone6X { get; set; }
    public int BattleZone6Z { get; set; }
    public byte BattleZone6LX { get; set; }
    public byte BattleZone6LZ { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<HomeData>
    {
        public void Configure(EntityTypeBuilder<HomeData> builder)
        {
            builder.HasKey(p => p.Nation);
            builder.Property(p => p.Nation).ValueGeneratedNever();
        }
    }
}
