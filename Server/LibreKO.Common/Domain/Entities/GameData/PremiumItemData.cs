using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class PremiumItemData
{
    public byte Type { get; set; }
    public double ExpRestorePercent { get; set; }
    public short NoahPercent { get; set; }
    public short DropPercent { get; set; }
    public int BonusLoyalty { get; set; }
    public short RepairDiscountPercent { get; set; }
    public short ItemSellPercent { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PremiumItemData>
    {
        public void Configure(EntityTypeBuilder<PremiumItemData> builder)
        {
            builder.HasKey(p => p.Type);
            builder.Property(p => p.Type).ValueGeneratedNever();
        }
    }
}

public class PremiumItemExpData
{
    public int Index { get; set; }
    public byte Type { get; set; }
    public byte MinLevel { get; set; }
    public byte MaxLevel { get; set; }
    public short Percent { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PremiumItemExpData>
    {
        public void Configure(EntityTypeBuilder<PremiumItemExpData> builder)
        {
            builder.HasKey(p => p.Index);
            builder.Property(p => p.Index).ValueGeneratedNever();
        }
    }
}
