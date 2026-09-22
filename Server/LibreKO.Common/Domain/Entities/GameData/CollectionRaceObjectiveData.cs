using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class CollectionRaceObjectiveData
{
    public int Id { get; set; }
    public int RaceId { get; set; }
    public byte Ordinal { get; set; }
    public CollectionRaceObjectiveKind Kind { get; set; }
    public int TargetId { get; set; }
    public int Count { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<CollectionRaceObjectiveData>
    {
        public void Configure(EntityTypeBuilder<CollectionRaceObjectiveData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => new { p.RaceId, p.Ordinal }).IsUnique();
            builder.HasOne<CollectionRaceData>()
                .WithMany()
                .HasForeignKey(p => p.RaceId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
