using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IDailyRankService
{
    Task HandleAsync(IClient client, Packet packet);
    Task CountMerchantSaleAsync(UserSession seller, long gold);
    Task CountBlessedUpgradeAsync(UserSession session);
    Task RecomputeAsync();
    int PlaceOf(DailyRankBoard board, int subjectId);
}

public sealed class DailyRankService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    ILogger<DailyRankService> logger) : BackgroundService, IDailyRankService
{
    private const int CheckSeconds = 60;
    private const long RequestGapMilliseconds = 1000;

    private sealed record BoardRanking(IReadOnlyList<string> Names, IReadOnlyDictionary<int, DailyRankPlace> Places);

    private readonly SemaphoreSlim counterLock = new(1, 1);
    private readonly SemaphoreSlim rankLock = new(1, 1);
    private readonly ConcurrentDictionary<int, long> lastRequestAt = new();
    private volatile IReadOnlyDictionary<DailyRankBoard, BoardRanking> boards = new Dictionary<DailyRankBoard, BoardRanking>();
    private DateOnly rankedOn;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SafelyAsync(() => LoadAsync(Today));
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(CheckSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            if (Today != rankedOn)
                await SafelyAsync(() => RollOverAsync(Today));
    }

    private async Task SafelyAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Daily ranking update failed");
        }
    }

    public async Task LoadAsync(DateOnly today)
    {
        await rankLock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDailyRankRepository>();
            var stored = await repository.GetPlacesAsync();
            if (stored.Count > 0 && stored.All(place => place.RankedOn == today))
            {
                boards = await BuildAsync(stored, repository);
                rankedOn = today;
                return;
            }
            await RankAsync(today, repository, stored);
        }
        finally
        {
            rankLock.Release();
        }
    }

    public async Task RollOverAsync(DateOnly today)
    {
        await rankLock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IDailyRankRepository>();
            await RankAsync(today, repository, await repository.GetPlacesAsync());
        }
        finally
        {
            rankLock.Release();
        }
        await sessionManager.BroadcastToAll(DailyRankPacketWriter.Reset());
        logger.LogInformation("Daily rankings recomputed for {Day}", today);
    }

    public Task RecomputeAsync() => RollOverAsync(Today);

    public int PlaceOf(DailyRankBoard board, int subjectId) =>
        subjectId != 0 && boards.TryGetValue(board, out var ranking) && ranking.Places.TryGetValue(subjectId, out var place)
            ? place.Place
            : 0;

    private async Task RankAsync(DateOnly today, IDailyRankRepository repository, IReadOnlyList<DailyRankPlace> stored)
    {
        var previous = stored.ToDictionary(
            place => (place.Board, place.SubjectId),
            place => place.RankedOn == today ? place.PreviousPlace : place.Place);
        var orders = new Dictionary<DailyRankBoard, IReadOnlyList<int>>
        {
            [DailyRankBoard.GrandMerchant] = await repository.GetMerchantOrderAsync(),
            [DailyRankBoard.KnightOfAdonis] = await repository.GetMonthlyLoyaltyOrderAsync(),
            [DailyRankBoard.DiscipleOfCharon] = await repository.GetBlessedUpgradeOrderAsync(),
            [DailyRankBoard.KnightsOfFelankor] = ClanOrder(),
        };

        var places = new List<DailyRankPlace>();
        foreach (var (board, order) in orders)
            for (var i = 0; i < order.Count; i++)
                places.Add(new DailyRankPlace
                {
                    Board = (byte)board,
                    SubjectId = order[i],
                    Place = i + 1,
                    PreviousPlace = previous.GetValueOrDefault(((byte)board, order[i])),
                    RankedOn = today,
                });
        await repository.ReplacePlacesAsync(places);

        var lastRankedOn = stored.Count > 0 ? stored.Max(place => place.RankedOn) : today;
        if (lastRankedOn.Year != today.Year || lastRankedOn.Month != today.Month)
            await repository.ResetCountersAsync();

        boards = await BuildAsync(places, repository);
        rankedOn = today;
    }

    private IReadOnlyList<int> ClanOrder() => sessionManager.Knights.GetAll()
        .Where(clan => clan.Points > 0)
        .OrderByDescending(clan => clan.Points).ThenBy(clan => clan.Id)
        .Select(clan => (int)clan.Id)
        .ToList();

    private async Task<IReadOnlyDictionary<DailyRankBoard, BoardRanking>> BuildAsync(
        IReadOnlyList<DailyRankPlace> places, IDailyRankRepository repository)
    {
        var listed = places.Where(place => place.Place <= DailyRankPacketWriter.MaxNames).ToList();
        var characterIds = listed.Where(place => place.Board != (byte)DailyRankBoard.KnightsOfFelankor)
            .Select(place => place.SubjectId).Distinct().ToList();
        var names = await repository.GetNamesAsync(characterIds);

        var boards = new Dictionary<DailyRankBoard, BoardRanking>();
        foreach (var group in places.GroupBy(place => (DailyRankBoard)place.Board))
        {
            var ordered = group.OrderBy(place => place.Place).ToList();
            var shown = ordered.Take(DailyRankPacketWriter.MaxNames)
                .Select(place => group.Key == DailyRankBoard.KnightsOfFelankor
                    ? sessionManager.Knights.GetClan(place.SubjectId)?.Name ?? string.Empty
                    : names.GetValueOrDefault(place.SubjectId, string.Empty))
                .ToList();
            boards[group.Key] = new BoardRanking(shown, ordered.ToDictionary(place => place.SubjectId));
        }
        return boards;
    }

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 2 || packet.ReadByte() != DailyRankPacketWriter.BoardSub)
            return;
        var raw = packet.ReadByte();
        if (!DailyRankPacketWriter.IsBoard(raw) || !TakeRequestSlot(session.CharacterId))
            return;

        var board = (DailyRankBoard)raw;
        var reply = board == DailyRankBoard.ConquerorOfDraki
            ? await DrakiBoardAsync(session)
            : StoredBoard(board, board == DailyRankBoard.KnightsOfFelankor ? session.KnightsId : session.CharacterId);
        await session.Client.SendPacket(reply);
    }

    private bool TakeRequestSlot(int characterId)
    {
        var now = Environment.TickCount64;
        if (lastRequestAt.TryGetValue(characterId, out var last) && now - last < RequestGapMilliseconds)
            return false;
        lastRequestAt[characterId] = now;
        return true;
    }

    private Packet StoredBoard(DailyRankBoard board, int subjectId)
    {
        if (!boards.TryGetValue(board, out var ranking))
            return DailyRankPacketWriter.Board(board, 0, 0, []);
        if (subjectId == 0 || !ranking.Places.TryGetValue(subjectId, out var mine))
            return DailyRankPacketWriter.Board(board, 0, 0, ranking.Names);
        return DailyRankPacketWriter.Board(board, mine.Place,
            DailyRankPacketWriter.Change(mine.Place, mine.PreviousPlace), ranking.Names);
    }

    private async Task<Packet> DrakiBoardAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var climbers = await scope.ServiceProvider.GetRequiredService<IDailyRankRepository>().GetDrakiClimbersAsync();
        var group = ClassIdHelper.GroupOf(session.Class);
        var sameClass = climbers.Where(climber => ClassIdHelper.GroupOf(climber.Class) == group).ToList();
        var place = sameClass.FindIndex(climber => climber.CharacterId == session.CharacterId) + 1;
        return DailyRankPacketWriter.Board(DailyRankBoard.ConquerorOfDraki, place, 0,
            sameClass.Take(DailyRankPacketWriter.MaxNames).Select(climber => climber.Name).ToList());
    }

    public Task CountMerchantSaleAsync(UserSession seller, long gold) =>
        seller.IsGM || gold <= 0
            ? Task.CompletedTask
            : CountAsync(repository => repository.AddMerchantGoldAsync(seller.CharacterId, gold));

    public Task CountBlessedUpgradeAsync(UserSession session) =>
        session.IsGM ? Task.CompletedTask : CountAsync(repository => repository.AddBlessedUpgradeAsync(session.CharacterId));

    private async Task CountAsync(Func<IDailyRankRepository, Task> update)
    {
        await counterLock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            await update(scope.ServiceProvider.GetRequiredService<IDailyRankRepository>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Daily ranking counter update failed");
        }
        finally
        {
            counterLock.Release();
        }
    }
}
