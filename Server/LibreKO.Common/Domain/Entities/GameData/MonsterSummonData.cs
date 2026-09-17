using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class MonsterSummonData
{
    public int Index { get; set; }
    public short Sid { get; set; }
    public string Name { get; set; } = string.Empty;
    public short Level { get; set; }
    public short Probability { get; set; }
    public byte Type { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MonsterSummonData>
    {
        public void Configure(EntityTypeBuilder<MonsterSummonData> builder)
        {
            builder.HasKey(p => p.Index);
            builder.Property(p => p.Index).ValueGeneratedNever();
        }
    }
}
