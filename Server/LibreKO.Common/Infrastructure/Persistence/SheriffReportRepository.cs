using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class SheriffReportRepository(AppDbContext context) : ISheriffReportRepository
{
    public async Task<SheriffReportEntity> CreateAsync(int reporterCharId, int targetCharId, string targetName, string reason)
    {
        var report = new SheriffReportEntity
        {
            ReporterCharId = reporterCharId,
            TargetCharId = targetCharId,
            TargetName = targetName,
            Reason = reason,
            VoteYesCount = 0,
            VoteNoCount = 0,
            Status = SheriffReportStatus.Open,
        };
        context.SheriffReports.Add(report);
        await context.SaveChangesAsync();
        return report;
    }

    public async Task<SheriffReportEntity?> GetById(int id)
        => await context.SheriffReports.FindAsync(id);

    public async Task<IReadOnlyList<SheriffReportEntity>> GetOpenPaged(int skip, int take)
        => await context.SheriffReports
            .Where(r => r.Status == SheriffReportStatus.Open)
            .OrderByDescending(r => r.Id)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 50))
            .ToListAsync();

    public async Task<int> CountOpen()
        => await context.SheriffReports.CountAsync(r => r.Status == SheriffReportStatus.Open);

    public async Task<bool> AddVoteAsync(int reportId, int voterCharId, bool voteYes)
    {
        var existing = await context.SheriffVotes
            .AnyAsync(v => v.ReportId == reportId && v.VoterCharId == voterCharId);
        if (existing) return false;

        context.SheriffVotes.Add(new SheriffVoteEntity
        {
            ReportId = reportId,
            VoterCharId = voterCharId,
            VoteYes = voteYes,
        });
        await context.SaveChangesAsync();
        return true;
    }

    public async Task UpdateAsync(SheriffReportEntity report)
    {
        context.SheriffReports.Update(report);
        await context.SaveChangesAsync();
    }
}
