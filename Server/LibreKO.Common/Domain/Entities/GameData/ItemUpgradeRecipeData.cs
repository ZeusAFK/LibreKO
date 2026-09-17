using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemUpgradeRecipeData
{
    public int Index { get; set; }
    public string Note { get; set; } = string.Empty;
    public int OriginNumber { get; set; }
    public string NewItemNote { get; set; } = string.Empty;
    public int NewNumber { get; set; }
    public int RequiredItem { get; set; }
    public byte Grade { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemUpgradeRecipeData>
    {
        public void Configure(EntityTypeBuilder<ItemUpgradeRecipeData> builder)
        {
            builder.HasKey(p => p.Index);
            builder.Property(p => p.Index).ValueGeneratedNever();
        }
    }
}
