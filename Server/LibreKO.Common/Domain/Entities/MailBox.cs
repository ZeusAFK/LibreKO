using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public class MailBox
{
    public int LetterId { get; set; }
    public DateTime SendDate { get; set; }
    public DateTime? ReadDate { get; set; }
    public byte Status { get; set; } // 1=unread, 2=read/claimed
    public string SenderId { get; set; } = string.Empty;
    public string RecipientId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public byte Type { get; set; } // 1=text only, 2=with item
    public int ItemId { get; set; }
    public short Count { get; set; }
    public short Durability { get; set; }
    public long SerialNumber { get; set; }
    public int Coins { get; set; }
    public bool Deleted { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<MailBox>
    {
        public void Configure(EntityTypeBuilder<MailBox> builder)
        {
            builder.HasKey(p => p.LetterId);
            builder.Property(p => p.LetterId).HasColumnName("nLetterID").ValueGeneratedOnAdd();
            builder.Property(p => p.SendDate).HasColumnName("dtSendDate");
            builder.Property(p => p.ReadDate).HasColumnName("dtReadDate");
            builder.Property(p => p.Status).HasColumnName("bStatus");
            builder.Property(p => p.SenderId).HasColumnName("strSenderID").HasMaxLength(50);
            builder.Property(p => p.RecipientId).HasColumnName("strRecipientID").HasMaxLength(50);
            builder.Property(p => p.Subject).HasColumnName("strSubject").HasMaxLength(50);
            builder.Property(p => p.Message).HasColumnName("strMessage").HasMaxLength(128);
            builder.Property(p => p.Type).HasColumnName("bType");
            builder.Property(p => p.ItemId).HasColumnName("nItemID");
            builder.Property(p => p.Count).HasColumnName("sCount");
            builder.Property(p => p.Durability).HasColumnName("sDurability");
            builder.Property(p => p.SerialNumber).HasColumnName("nSerialNum");
            builder.Property(p => p.Coins).HasColumnName("nCoins");
            builder.Property(p => p.Deleted).HasColumnName("bDeleted");
        }
    }
}
