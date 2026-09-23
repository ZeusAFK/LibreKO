using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class LotteryRewardData
{
    public int Id { get; set; }
    public int LotteryId { get; set; }
    public int Place { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<LotteryRewardData>
    {
        public void Configure(EntityTypeBuilder<LotteryRewardData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => p.LotteryId);
            builder.HasOne<LotteryEventData>()
                .WithMany()
                .HasForeignKey(p => p.LotteryId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
