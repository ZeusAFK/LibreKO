using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class LevelUpData
{
    public byte Level { get; set; }
    public long Exp { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<LevelUpData>
    {
        public void Configure(EntityTypeBuilder<LevelUpData> builder)
        {
            builder.HasKey(p => p.Level);
            builder.Property(p => p.Level).ValueGeneratedNever();
        }
    }
}
