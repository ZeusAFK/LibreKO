using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Common.Infrastructure.Persistence;

public class EventPostRepository(AppDbContext context) : IEventPostRepository
{
    public async Task<EventPostBoard> BoardAsync(byte eventId, DateTime dayStart, DateTime periodStart, DateTime periodEnd, int recentLimit)
    {
        var inPeriod = context.EventPosts.AsNoTracking()
            .Where(post => post.EventId == eventId && post.PostedAt >= periodStart && post.PostedAt < periodEnd);
        var today = await inPeriod.CountAsync(post => post.PostedAt >= dayStart);
        var total = await inPeriod.CountAsync();
        var recent = await inPeriod.OrderByDescending(post => post.PostedAt).ThenByDescending(post => post.Id)
            .Take(recentLimit).ToListAsync();
        return new EventPostBoard(today, total, recent);
    }

    public Task<int> CountAsync(int characterId, byte eventId, DateTime from, DateTime to) =>
        context.EventPosts.CountAsync(post => post.CharacterId == characterId && post.EventId == eventId
                                              && post.PostedAt >= from && post.PostedAt < to);

    public async Task<IReadOnlyList<EventPost>> HistoryAsync(int characterId, int limit) =>
        await context.EventPosts.AsNoTracking()
            .Where(post => post.CharacterId == characterId)
            .OrderByDescending(post => post.PostedAt).ThenByDescending(post => post.Id)
            .Take(limit).ToListAsync();

    public async Task AddAsync(EventPost post)
    {
        context.EventPosts.Add(post);
        await context.SaveChangesAsync();
    }
}
