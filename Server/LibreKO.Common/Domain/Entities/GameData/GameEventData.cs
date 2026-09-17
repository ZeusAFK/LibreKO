using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class GameEventData
{
    public byte ZoneNum { get; set; }
    public short EventNum { get; set; }
    public byte Type { get; set; }
    public int Cond1 { get; set; }
    public int Cond2 { get; set; }
    public int Cond3 { get; set; }
    public int Cond4 { get; set; }
    public int Cond5 { get; set; }
    public int Exec1 { get; set; }
    public int Exec2 { get; set; }
    public int Exec3 { get; set; }
    public int Exec4 { get; set; }
    public int Exec5 { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<GameEventData>
    {
        public void Configure(EntityTypeBuilder<GameEventData> builder)
        {
            builder.HasKey(p => new { p.ZoneNum, p.EventNum });
        }
    }
}
