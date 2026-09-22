using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public static class MailLimits
{
    public const int SubjectMax = 64;
    public const int BodyMax = 512;
    public const int SenderNameMax = 50;
    public const int ItemAttachmentsMax = 4;
    public const int InboxMax = 50;
    public const string SystemSenderName = "LibreKO";
}

public class Mail
{
    public int Id { get; set; }
    public int? SenderCharacterId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public int RecipientCharacterId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public bool Deleted { get; set; }
    public List<MailAttachment> Attachments { get; set; } = [];

    public bool HasUnclaimedAttachments => Attachments.Count > 0 && ClaimedAt == null;

    internal class EntityConfiguration : IEntityTypeConfiguration<Mail>
    {
        public void Configure(EntityTypeBuilder<Mail> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.SenderName).HasMaxLength(MailLimits.SenderNameMax);
            builder.Property(p => p.Subject).HasMaxLength(MailLimits.SubjectMax);
            builder.Property(p => p.Body).HasMaxLength(MailLimits.BodyMax);
            builder.HasIndex(p => new { p.RecipientCharacterId, p.Deleted });
            builder.HasMany(p => p.Attachments)
                .WithOne()
                .HasForeignKey(a => a.MailId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
