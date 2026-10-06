using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemCombineRecipeData
{
    public const int RateScale = 10_000;

    public int Id { get; set; }
    public int NpcId { get; set; }
    public short DisplayRow { get; set; }
    public int ResultItemId { get; set; }
    public int ResultCount { get; set; }
    public int SuccessRate { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemCombineRecipeData>
    {
        public void Configure(EntityTypeBuilder<ItemCombineRecipeData> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
