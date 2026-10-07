using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ForgottenTempleWaveData
{
    public int Id { get; set; }
    public byte Tier { get; set; }
    public byte Wave { get; set; }
    public int StartSecond { get; set; }
    public int NpcId { get; set; }
    public byte Count { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<ForgottenTempleWaveData>
    {
        public void Configure(EntityTypeBuilder<ForgottenTempleWaveData> builder)
        {
            builder.ToTable("ForgottenTempleWaves");
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.HasIndex(p => new { p.Tier, p.Wave });
        }
    }
}
