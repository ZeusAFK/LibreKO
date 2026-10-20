using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class Bounty
{
    public const int NameMax = 21;

    public int Id { get; set; }
    public int PosterCharacterId { get; set; }
    public string PosterName { get; set; } = string.Empty;
    public int TargetCharacterId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public int Reward { get; set; }
    public DateTime PostedAt { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<Bounty>
    {
        public void Configure(EntityTypeBuilder<Bounty> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.PosterName).HasMaxLength(NameMax);
            builder.Property(p => p.TargetName).HasMaxLength(NameMax);
            builder.HasIndex(p => p.TargetCharacterId);
        }
    }
}
