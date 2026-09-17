using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface IKingElectionRepository
{
    Task<KingElectionList?> FindCandidateAsync(byte nation, byte candidateType, string name);
    Task<List<KingElectionList>> GetCandidatesAsync(byte nation, byte candidateType);
    Task AddCandidateAsync(KingElectionList candidate);
    Task RemoveCandidateAsync(KingElectionList candidate);
    Task<bool> IsCandidateAsync(byte nation, byte candidateType, string name);
    Task<KingCandidacyNoticeBoard?> GetNoticeBoardEntryAsync(byte nation, string userId);
    Task UpsertNoticeBoardAsync(KingCandidacyNoticeBoard entry);
    Task<bool> HasVotedAsync(byte nation, string charName);
    Task AddVoteAsync(KingBallotBox vote);
}
