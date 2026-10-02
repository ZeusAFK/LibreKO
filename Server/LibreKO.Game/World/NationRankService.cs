using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public readonly record struct NationRanks(byte Knights, byte Personal)
{
    public const byte Unranked = byte.MaxValue;

    public static readonly NationRanks None = new(Unranked, Unranked);

    public NationRanks Shown => new(
        Knights <= Personal ? Knights : Unranked,
        Personal <= Knights ? Personal : Unranked);
}

public interface INationRankService
{
    NationRanks Of(int characterId);
    Task RefreshAsync();
}

public class NationRankService(
    IServiceScopeFactory scopeFactory,
    IServiceProvider services,
    SessionManager sessionManager,
    ILogger<NationRankService> logger) : BackgroundService, INationRankService
{
    private const int IntervalSeconds = 3600;
    public const int KnightsRankPlaces = 100;
    public const int PersonalRankPlaces = 200;

    private static readonly AccountNation[] Nations = [AccountNation.Karus, AccountNation.ElMorad];

    private volatile IReadOnlyDictionary<int, NationRanks> _ranks = new Dictionary<int, NationRanks>();

    public NationRanks Of(int characterId) =>
        _ranks.TryGetValue(characterId, out var ranks) ? ranks : NationRanks.None;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshSafelyAsync();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(IntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RefreshSafelyAsync();
    }

    private async Task RefreshSafelyAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "National rank refresh failed");
        }
    }

    public async Task RefreshAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var characters = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var knights = new Dictionary<int, byte>();
        var personal = new Dictionary<int, byte>();
        var leaders = new Dictionary<AccountNation, IReadOnlyList<int>>();
        foreach (var nation in Nations)
        {
            Place(knights, await characters.GetPlayerIdsByLoyalty(nation, KnightsRankPlaces, monthly: false));
            var monthly = await characters.GetPlayerIdsByLoyalty(nation, PersonalRankPlaces, monthly: true);
            Place(personal, monthly);
            leaders[nation] = monthly;
        }

        var ranks = new Dictionary<int, NationRanks>();
        foreach (var id in knights.Keys.Union(personal.Keys))
            ranks[id] = new NationRanks(
                knights.GetValueOrDefault(id, NationRanks.Unranked),
                personal.GetValueOrDefault(id, NationRanks.Unranked));
        _ranks = ranks;

        var visibility = services.GetService<IWorldVisibilityService>();
        foreach (var session in sessionManager.GetAll())
        {
            var placed = Of(session.CharacterId);
            if (placed == session.NationRanks) continue;
            session.NationRanks = placed;
            if (visibility != null) await visibility.ResendUserRecordAsync(session);
        }

        if (services.GetService<IRankerStatueService>() is { } statues)
            await statues.RefreshAsync(leaders);
    }

    private static void Place(Dictionary<int, byte> places, IReadOnlyList<int> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
            places[ordered[i]] = (byte)(i + 1);
    }
}
