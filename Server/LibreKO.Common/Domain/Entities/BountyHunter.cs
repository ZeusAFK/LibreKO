using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class BountyHunter
{
    public int BountyId { get; set; }
    public int CharacterId { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<BountyHunter>
    {
        public void Configure(EntityTypeBuilder<BountyHunter> builder)
        {
            builder.HasKey(p => new { p.BountyId, p.CharacterId });
        }
    }
}
