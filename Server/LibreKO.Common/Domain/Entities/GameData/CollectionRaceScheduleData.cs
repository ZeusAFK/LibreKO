using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class CollectionRaceScheduleData
{
    public int Id { get; set; }
    public int RaceId { get; set; }
    public DayOfWeek? Day { get; set; }
    public byte Hour { get; set; }
    public byte Minute { get; set; }

    public bool Matches(DateTime utcNow) =>
        (Day == null || Day == utcNow.DayOfWeek) && Hour == utcNow.Hour && Minute == utcNow.Minute;

    internal class EntityConfiguration : IEntityTypeConfiguration<CollectionRaceScheduleData>
    {
        public void Configure(EntityTypeBuilder<CollectionRaceScheduleData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => p.RaceId);
            builder.HasOne<CollectionRaceData>()
                .WithMany()
                .HasForeignKey(p => p.RaceId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
