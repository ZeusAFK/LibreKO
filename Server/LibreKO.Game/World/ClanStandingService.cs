using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IClanStandingService
{
    Task RefreshClanAsync(short clanId);
    Task RefreshRankingsAsync();
    Task RefreshAllAsync();
    void MarkDirty(short clanId);
}

public class ClanStandingService(
    IServiceScopeFactory scopeFactory,
    SessionManager sessionManager,
    ILogger<ClanStandingService> logger) : BackgroundService, IClanStandingService
{
    private const int IntervalSeconds = 3600;

    private readonly ConcurrentDictionary<short, byte> _dirty = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void MarkDirty(short clanId) => _dirty[clanId] = 1;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RefreshAllAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Clan standing refresh at startup failed");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RefreshAllAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Clan standing refresh failed");
            }
        }
    }

    public async Task RefreshClanAsync(short clanId)
    {
        var clan = sessionManager.Knights.GetClan(clanId);
        if (clan == null)
            return;

        await _gate.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
            var members = await repo.GetMembersAsync(clanId);

            var changed = new List<KnightsEntity>();
            if (ApplyPoints(clan, SumLoyalty(members)))
                changed.Add(clan);
            ApplyRankings(changed);

            await PersistAsync(repo, changed);
            await AnnounceAsync(changed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshRankingsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
            var changed = new List<KnightsEntity>();
            ApplyRankings(changed);
            await PersistAsync(repo, changed);
            await AnnounceAsync(changed);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IKnightsRepository>();
            var loyalty = await repo.GetMemberLoyaltyAsync();
            var totals = new Dictionary<short, long>();
            foreach (var member in loyalty)
            {
                var online = sessionManager.GetByName(member.Name);
                totals[member.ClanId] = totals.GetValueOrDefault(member.ClanId)
                    + (online?.Loyalty ?? member.Loyalty);
            }

            var changed = new List<KnightsEntity>();
            foreach (var clan in sessionManager.Knights.GetAll())
            {
                var points = (int)Math.Min(totals.GetValueOrDefault(clan.Id), int.MaxValue);
                if (ApplyPoints(clan, points))
                    changed.Add(clan);
            }

            ApplyRankings(changed);
            await PersistAsync(repo, changed);
            await AnnounceAsync(changed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private int SumLoyalty(IEnumerable<ClanMemberProjection> members)
    {
        long total = 0;
        foreach (var member in members)
        {
            var online = sessionManager.GetByName(member.Name);
            total += online?.Loyalty ?? member.Loyalty;
        }

        return (int)Math.Min(total, int.MaxValue);
    }

    private static bool ApplyPoints(KnightsEntity clan, int points)
    {
        var grade = ClanRules.GradeFromPoints(points);
        var changed = clan.Points != points || clan.Grade != grade;
        clan.Points = points;
        clan.Grade = grade;
        return changed;
    }

    private void ApplyRankings(List<KnightsEntity> changed)
    {
        var ranked = sessionManager.Knights.GetAll()
            .Where(clan => clan.ClanPointFund > 0)
            .OrderByDescending(clan => clan.ClanPointFund)
            .ThenBy(clan => clan.Id)
            .Take(ClanRules.RankedClans)
            .ToList();

        var rankings = new Dictionary<short, byte>();
        byte rank = 1;
        foreach (var clan in ranked)
            rankings[clan.Id] = rank++;

        foreach (var clan in sessionManager.Knights.GetAll())
        {
            var ranking = rankings.TryGetValue(clan.Id, out var value) ? value : ClanRules.Unranked;
            if (clan.Ranking == ranking)
                continue;

            clan.Ranking = ranking;
            if (!changed.Contains(clan))
                changed.Add(clan);
        }
    }

    private async Task PersistAsync(IKnightsRepository repo, List<KnightsEntity> changed)
    {
        foreach (var clanId in _dirty.Keys.ToList())
        {
            _dirty.TryRemove(clanId, out _);
            var clan = sessionManager.Knights.GetClan(clanId);
            if (clan != null && !changed.Contains(clan))
                changed.Add(clan);
        }

        foreach (var clan in changed)
        {
            try
            {
                await repo.UpdateAsync(clan);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not persist the standing of clan {Clan}", clan.Name);
            }
        }
    }

    private async Task AnnounceAsync(List<KnightsEntity> changed)
    {
        if (changed.Count == 0)
            return;

        var entries = changed
            .Select(clan => new KnightsPacketWriter.StandingEntry(clan.Id, clan.Grade, clan.Ranking))
            .ToList();
        await sessionManager.BroadcastToAll(KnightsPacketWriter.StandingRefresh(entries));
    }
}
