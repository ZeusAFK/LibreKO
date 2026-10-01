using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class PetLevelData
{
    public byte Level { get; set; }
    public short MaxHp { get; set; }
    public short MaxMp { get; set; }
    public short Attack { get; set; }
    public short Defence { get; set; }
    public byte Resist { get; set; }
    public long Exp { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<PetLevelData>
    {
        public void Configure(EntityTypeBuilder<PetLevelData> builder)
        {
            builder.HasKey(p => p.Level);
            builder.Property(p => p.Level).ValueGeneratedNever();
        }
    }
}
