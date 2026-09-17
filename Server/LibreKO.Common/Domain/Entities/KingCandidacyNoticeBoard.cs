using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class KingCandidacyNoticeBoard
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public byte Nation { get; set; }
    public short NoticeLen { get; set; }
    public byte[] Notice { get; set; } = [];

    internal class EntityConfiguration : IEntityTypeConfiguration<KingCandidacyNoticeBoard>
    {
        public void Configure(EntityTypeBuilder<KingCandidacyNoticeBoard> builder)
        {
            builder.HasKey(p => p.Id);
        }
    }
}
