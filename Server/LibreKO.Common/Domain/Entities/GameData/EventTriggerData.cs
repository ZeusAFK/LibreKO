using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class EventTriggerData
{
    public const int NoTrigger = -1;

    public int Index { get; set; }
    public short NpcType { get; set; }
    public int NpcId { get; set; }
    public int TriggerNum { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<EventTriggerData>
    {
        public void Configure(EntityTypeBuilder<EventTriggerData> builder)
        {
            builder.HasKey(p => p.Index);
            builder.Property(p => p.Index).ValueGeneratedNever();
        }
    }
}
