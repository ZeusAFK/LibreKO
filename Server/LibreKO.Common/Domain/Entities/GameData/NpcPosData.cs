using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class NpcPosData
{
    public const byte NpcSpawnActTypeBase = 100;

    public int Index { get; set; }
    public short ZoneId { get; set; }
    public int NpcId { get; set; }
    public byte ActType { get; set; }       // MoveType: 0-4 for monsters, 100+ for NPCs (subtract 100)
    public byte DotCnt { get; set; }        // Number of waypoints
    public string? Path { get; set; }       // Waypoint data: 4-char X + 4-char Z per point
    public int LeftX { get; set; }
    public int TopZ { get; set; }           // Spawn centre Z
    public byte NumNPC { get; set; }        // Number of NPCs to spawn
    public short RegTime { get; set; }      // Respawn time in seconds
    public int Direction { get; set; }

    // of radius SpawnRange centred at (LeftX, TopZ). Used for instanced content
    // (Chaos Stone, Forgotten Temple, Dungeon Defence, Juraid).
    public short SpawnRange { get; set; }    // Radius around (LeftX, TopZ). 0 = single fixed point.
    public short RegenType { get; set; }
    public short DungeonFamily { get; set; }
    public short SpecialType { get; set; }
    public short TrapNumber { get; set; }
    public short Room { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<NpcPosData>
    {
        public void Configure(EntityTypeBuilder<NpcPosData> builder)
        {
            builder.HasKey(p => p.Index);
            builder.Property(p => p.Index).ValueGeneratedNever();
        }
    }
}
