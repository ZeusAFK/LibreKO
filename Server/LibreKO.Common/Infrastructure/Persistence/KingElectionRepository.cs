using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class KingElectionRepository(AppDbContext context) : IKingElectionRepository
{
    public async Task<KingElectionList?> FindCandidateAsync(byte nation, byte candidateType, string name)
    {
        return await context.KingElectionList
            .OrderBy(e => e.Name)
            .FirstOrDefaultAsync(e =>
                e.Nation == nation
                && e.Type == candidateType
                && e.Name == name);
    }

    public async Task<List<KingElectionList>> GetCandidatesAsync(byte nation, byte candidateType)
    {
        return await context.KingElectionList
            .Where(e => e.Nation == nation && e.Type == candidateType)
            .ToListAsync();
    }

    public async Task AddCandidateAsync(KingElectionList candidate)
    {
        context.KingElectionList.Add(candidate);
        await context.SaveChangesAsync();
    }

    public async Task RemoveCandidateAsync(KingElectionList candidate)
    {
        context.KingElectionList.Remove(candidate);
        await context.SaveChangesAsync();
    }

    public async Task<bool> IsCandidateAsync(byte nation, byte candidateType, string name)
    {
        return await context.KingElectionList.AnyAsync(e =>
            e.Nation == nation
            && e.Type == candidateType
            && e.Name == name);
    }

    public async Task<KingCandidacyNoticeBoard?> GetNoticeBoardEntryAsync(byte nation, string userId)
    {
        return await context.KingCandidacyNoticeBoard
            .OrderBy(e => e.UserId)
            .FirstOrDefaultAsync(e => e.Nation == nation && e.UserId == userId);
    }

    public async Task UpsertNoticeBoardAsync(KingCandidacyNoticeBoard entry)
    {
        var existing = await context.KingCandidacyNoticeBoard
            .OrderBy(e => e.UserId)
            .FirstOrDefaultAsync(e => e.Nation == entry.Nation && e.UserId == entry.UserId);

        if (existing != null)
        {
            existing.NoticeLen = entry.NoticeLen;
            existing.Notice = entry.Notice;
        }
        else
        {
            context.KingCandidacyNoticeBoard.Add(entry);
        }

        await context.SaveChangesAsync();
    }

    public async Task<bool> HasVotedAsync(byte nation, string charName)
    {
        return await context.KingBallotBox.AnyAsync(e =>
            e.Nation == nation && e.CharId == charName);
    }

    public async Task AddVoteAsync(KingBallotBox vote)
    {
        context.KingBallotBox.Add(vote);
        await context.SaveChangesAsync();
    }
}
