using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class CollectionRaceSettingsData
{
    public int EventIndex { get; set; }
    public string EventName { get; set; } = string.Empty;
    public byte ZoneId { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public int DurationMinutes { get; set; }

    public int Target1ProtoId { get; set; }
    public int Target1Count { get; set; }

    public int Target2ProtoId { get; set; }
    public int Target2Count { get; set; }

    public int Target3ProtoId { get; set; }
    public int Target3Count { get; set; }

    public int EnemyKillCount { get; set; }

    public bool AutoStart { get; set; }
    public string AutoHours { get; set; } = string.Empty;
    public string AutoDays { get; set; } = "All";

    internal class EntityConfiguration : IEntityTypeConfiguration<CollectionRaceSettingsData>
    {
        public void Configure(EntityTypeBuilder<CollectionRaceSettingsData> builder)
        {
            builder.HasKey(p => p.EventIndex);
            builder.Property(p => p.EventIndex).ValueGeneratedNever();
            builder.Property(p => p.EventName).HasMaxLength(128);
            builder.Property(p => p.AutoHours).HasMaxLength(64);
            builder.Property(p => p.AutoDays).HasMaxLength(64);
        }
    }
}
