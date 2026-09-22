using LibreKO.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class MailAttachment
{
    public int Id { get; set; }
    public int MailId { get; set; }
    public MailAttachmentKind Kind { get; set; }
    public int ItemId { get; set; }
    public int Count { get; set; }
    public short Durability { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MailAttachment>
    {
        public void Configure(EntityTypeBuilder<MailAttachment> builder)
        {
            builder.HasKey(p => p.Id);
            builder.HasIndex(p => p.MailId);
        }
    }
}
