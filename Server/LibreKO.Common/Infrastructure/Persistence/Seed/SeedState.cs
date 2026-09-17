using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Infrastructure.Persistence.Seed;

public class SeedState
{
    public const int NameLength = 128;
    public const int FingerprintLength = 64;

    public string Name { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public DateTime AppliedAt { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<SeedState>
    {
        public void Configure(EntityTypeBuilder<SeedState> builder)
        {
            builder.HasKey(p => p.Name);
            builder.Property(p => p.Name).HasMaxLength(NameLength).ValueGeneratedNever();
            builder.Property(p => p.Fingerprint).HasMaxLength(FingerprintLength);
        }
    }
}
