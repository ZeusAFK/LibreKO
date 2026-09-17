using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemOpData
{
    public int ItemId { get; set; }
    public byte TriggerType { get; set; }  // 3=on equip, 13=on use
    public int SkillId { get; set; }
    public byte TriggerRate { get; set; }  // probability (1-100)

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemOpData>
    {
        public void Configure(EntityTypeBuilder<ItemOpData> builder)
        {
            builder.HasKey(p => new { p.ItemId, p.TriggerType, p.SkillId });
        }
    }
}
