using LibreKO.Common.Domain.Entities;

namespace LibreKO.Common.Domain.Services;

public sealed record EventPostBoard(int Today, int InPeriod, IReadOnlyList<EventPost> Recent);

public interface IEventPostRepository
{
    Task<EventPostBoard> BoardAsync(byte eventId, DateTime dayStart, DateTime periodStart, DateTime periodEnd, int recentLimit);
    Task<int> CountAsync(int characterId, byte eventId, DateTime from, DateTime to);
    Task<IReadOnlyList<EventPost>> HistoryAsync(int characterId, int limit);
    Task AddAsync(EventPost post);
}
