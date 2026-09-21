using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class CollectionRaceRewardData
{
    public int Id { get; set; }
    public int EventIndex { get; set; }
    public int ItemId { get; set; }
    public int ItemCount { get; set; }
    public byte Rate { get; set; } = 100;

    internal class EntityConfiguration : IEntityTypeConfiguration<CollectionRaceRewardData>
    {
        public void Configure(EntityTypeBuilder<CollectionRaceRewardData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => p.EventIndex);
        }
    }
}
