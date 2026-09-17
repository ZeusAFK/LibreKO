using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LibreKO.Common.Domain.Entities;

public enum SheriffReportStatus : byte
{
    Open = 0,
    Approved = 1,   // 3+ yes votes → target imprisoned
    Dismissed = 2,  // 2+ no votes → report dismissed
}

public class SheriffReportEntity : Entity
{
    public int ReporterCharId { get; set; }
    public int TargetCharId { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public byte VoteYesCount { get; set; }
    public byte VoteNoCount { get; set; }
    public SheriffReportStatus Status { get; set; } = SheriffReportStatus.Open;

    internal class EntityConfiguration : IEntityTypeConfiguration<SheriffReportEntity>
    {
        public void Configure(EntityTypeBuilder<SheriffReportEntity> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.ReporterCharId).IsRequired();
            builder.Property(p => p.TargetCharId).IsRequired();
            builder.Property(p => p.TargetName).IsRequired().HasMaxLength(50);
            builder.Property(p => p.Reason).IsRequired().HasMaxLength(512);
            builder.Property(p => p.VoteYesCount);
            builder.Property(p => p.VoteNoCount);
            builder.Property(p => p.Status).IsRequired().HasConversion<byte>();
            builder.Property(p => p.CreatedAt).IsRequired();
            builder.HasIndex(p => p.Status);
            builder.HasIndex(p => p.TargetCharId);
        }
    }
}

public class SheriffVoteEntity : Entity
{
    public int ReportId { get; set; }
    public int VoterCharId { get; set; }
    public bool VoteYes { get; set; }

    internal class EntityConfiguration : IEntityTypeConfiguration<SheriffVoteEntity>
    {
        public void Configure(EntityTypeBuilder<SheriffVoteEntity> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.ReportId).IsRequired();
            builder.Property(p => p.VoterCharId).IsRequired();
            builder.Property(p => p.VoteYes).IsRequired();
            builder.Property(p => p.CreatedAt).IsRequired();
            builder.HasIndex(p => new { p.ReportId, p.VoterCharId }).IsUnique();
        }
    }
}
