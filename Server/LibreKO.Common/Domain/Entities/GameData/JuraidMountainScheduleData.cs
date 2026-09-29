using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class JuraidMountainScheduleData
{
    public int Id { get; set; }
    public DayOfWeek? Day { get; set; }
    public byte Hour { get; set; }
    public byte Minute { get; set; }
    public byte MinLevel { get; set; } = 40;
    public byte MaxLevel { get; set; } = 83;
    public byte CountdownMinutes { get; set; } = 10;

    public bool Matches(DateTime time) =>
        (Day == null || Day == time.DayOfWeek) && Hour == time.Hour && Minute == time.Minute;

    internal class EntityConfiguration : IEntityTypeConfiguration<JuraidMountainScheduleData>
    {
        public void Configure(EntityTypeBuilder<JuraidMountainScheduleData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
