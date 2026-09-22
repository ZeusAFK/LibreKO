using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class CollectionRaceData
{
    public const int DefaultDurationMinutes = 60;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public byte ZoneId { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public int DurationMinutes { get; set; } = DefaultDurationMinutes;
    public bool AutoStart { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<CollectionRaceData>
    {
        public void Configure(EntityTypeBuilder<CollectionRaceData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.Name).HasMaxLength(128);
            builder.HasIndex(p => p.ZoneId);
        }
    }
}
