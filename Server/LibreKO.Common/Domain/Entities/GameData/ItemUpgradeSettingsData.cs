using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemUpgradeSettingsData
{
    public const short AnyGrade = 99;

    public int Index { get; set; }
    public int ReqItem1 { get; set; }
    public int ReqItem2 { get; set; }
    public short ItemType { get; set; }
    public short ItemRate { get; set; }
    public short ItemGrade { get; set; }
    public int ReqNoah { get; set; }
    public int SuccessRate { get; set; }
    public short Status { get; set; }

    public bool MatchesGrade(short grade) => ItemGrade == grade || ItemGrade == AnyGrade;

    public bool MatchesMaterials(int first, int second)
        => (ReqItem1 == first || ReqItem2 == first) && (ReqItem1 == second || ReqItem2 == second);

    internal class EntityConfiguration : IEntityTypeConfiguration<ItemUpgradeSettingsData>
    {
        public void Configure(EntityTypeBuilder<ItemUpgradeSettingsData> builder)
        {
            builder.HasKey(p => p.Index);
            builder.Property(p => p.Index).ValueGeneratedNever();
            builder.HasIndex(p => new { p.ItemType, p.ItemGrade });
        }
    }
}
