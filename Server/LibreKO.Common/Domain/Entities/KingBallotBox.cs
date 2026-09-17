using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class KingBallotBox
{
    public int Id { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string CharId { get; set; } = string.Empty;
    public byte Nation { get; set; }
    public string CandidacyId { get; set; } = string.Empty;

    internal class EntityConfiguration : IEntityTypeConfiguration<KingBallotBox>
    {
        public void Configure(EntityTypeBuilder<KingBallotBox> builder)
        {
            builder.HasKey(p => p.Id);
        }
    }
}
