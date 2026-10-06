using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemCombineMaterialData
{
    public int RecipeId { get; set; }
    public byte Position { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemCombineMaterialData>
    {
        public void Configure(EntityTypeBuilder<ItemCombineMaterialData> builder)
        {
            builder.HasKey(p => new { p.RecipeId, p.Position });
        }
    }
}
