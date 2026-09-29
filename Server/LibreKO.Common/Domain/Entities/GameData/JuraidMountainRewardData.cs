using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class JuraidMountainRewardData
{
    public int Id { get; set; }
    public string Outcome { get; set; } = "";
    public int ItemId { get; set; }
    public int ItemCount { get; set; }
    public int LoyaltyPoints { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<JuraidMountainRewardData>
    {
        public void Configure(EntityTypeBuilder<JuraidMountainRewardData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => p.Outcome);
        }
    }
}
