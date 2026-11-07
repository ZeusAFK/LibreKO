using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class EventPost
{
    public const int NameMaxLength = 21;
    public const int ServerNameMaxLength = 50;
    public const int TextMaxLength = 64;

    public long Id { get; set; }
    public byte EventId { get; set; }
    public int CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime PostedAt { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<EventPost>
    {
        public void Configure(EntityTypeBuilder<EventPost> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.CharacterName).HasMaxLength(NameMaxLength);
            builder.Property(p => p.ServerName).HasMaxLength(ServerNameMaxLength);
            builder.Property(p => p.Text).HasMaxLength(TextMaxLength);
            builder.HasIndex(p => new { p.EventId, p.PostedAt });
            builder.HasIndex(p => new { p.CharacterId, p.PostedAt });
        }
    }
}
