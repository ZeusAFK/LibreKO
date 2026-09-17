using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public interface ISheriffReportRepository
{
    Task<SheriffReportEntity> CreateAsync(int reporterCharId, int targetCharId, string targetName, string reason);
    Task<SheriffReportEntity?> GetById(int id);
    Task<IReadOnlyList<SheriffReportEntity>> GetOpenPaged(int skip, int take);
    Task<int> CountOpen();
    Task<bool> AddVoteAsync(int reportId, int voterCharId, bool voteYes);
    Task UpdateAsync(SheriffReportEntity report);
}
