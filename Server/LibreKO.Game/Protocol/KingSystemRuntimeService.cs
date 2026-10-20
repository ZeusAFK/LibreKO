using System.Linq.Expressions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IKingSystemRuntimeService
{
    KingSystemData? GetKingData(AccountNation nation);
    bool IsKing(UserSession session, KingSystemData? kingData);
    Task PersistKingPropertyAsync<TProperty>(
        KingSystemData kingData,
        Expression<Func<KingSystemData, TProperty>> propertySelector);
    Task BroadcastToNationAsync(AccountNation nation, Packet packet);
}

public class KingSystemRuntimeService(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IGameDataService gameDataService,
    ILogger<KingSystemRuntimeService> logger) : IKingSystemRuntimeService
{
    public KingSystemData? GetKingData(AccountNation nation)
    {
        return gameDataService.KingSystemTable.TryGetValue((byte)nation, out var kingData) ? kingData : null;
    }

    public bool IsKing(UserSession session, KingSystemData? kingData) => IsKingName(kingData, session.Name);

    public const ushort KarusKingCape = 97;
    public const ushort ElmoradKingCape = 98;

    private static readonly Lock Purses = new();
    private static readonly SemaphoreSlim Writes = new(1, 1);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(byte Nation, string Property), byte> Waiting = new();

    // The treasury, tax and tribute change only under one lock.
    public static bool TrySpendTreasury(KingSystemData kingData, int amount)
    {
        lock (Purses)
        {
            if (amount <= 0 || kingData.NationalTreasury < amount)
                return false;

            kingData.NationalTreasury -= amount;
            return true;
        }
    }

    public static void ReturnToTreasury(KingSystemData kingData, int amount)
    {
        lock (Purses)
            kingData.NationalTreasury = (int)Math.Min((long)kingData.NationalTreasury + amount, ExchangePacketConstants.CoinMax);
    }

    public static void AddTax(KingSystemData kingData, int treasury, int territory)
    {
        lock (Purses)
        {
            kingData.NationalTreasury = (int)Math.Min((long)kingData.NationalTreasury + treasury, ExchangePacketConstants.CoinMax);
            kingData.TerritoryTax = (int)Math.Min((long)kingData.TerritoryTax + territory, ExchangePacketConstants.CoinMax);
        }
    }

    public static (int FromTax, int FromTribute) CollectFund(KingSystemData kingData, long room)
    {
        lock (Purses)
        {
            var fromTax = (int)Math.Min(Math.Max(kingData.TerritoryTax, 0), room);
            var fromTribute = (int)Math.Min(Math.Max(kingData.Tribute, 0), room - fromTax);
            kingData.TerritoryTax -= fromTax;
            kingData.Tribute -= fromTribute;
            return (fromTax, fromTribute);
        }
    }

    public static bool IsKingName(KingSystemData? kingData, string name) =>
        kingData != null && string.Equals(kingData.KingName?.Trim(), name, StringComparison.OrdinalIgnoreCase);

    public static bool IsKingOf(IGameDataService gameData, AccountNation nation, string name) =>
        gameData.KingSystemTable.TryGetValue((byte)nation, out var kingData) && IsKingName(kingData, name);

    public static ushort KingCapeOf(AccountNation nation) =>
        nation == AccountNation.ElMorad ? ElmoradKingCape : KarusKingCape;

    // Writes run one at a time, each storing the value as it stands when it runs.
    // A write already waiting stores the newest value when it runs, so another one for the same value adds nothing.
    public async Task PersistKingPropertyAsync<TProperty>(
        KingSystemData kingData,
        Expression<Func<KingSystemData, TProperty>> propertySelector)
    {
        var waiting = (kingData.Nation, ((MemberExpression)propertySelector.Body).Member.Name);
        if (!Waiting.TryAdd(waiting, 0))
            return;

        var read = propertySelector.Compile();
        await Writes.WaitAsync();
        try
        {
            Waiting.TryRemove(waiting, out _);
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = new KingSystemData { Nation = kingData.Nation };
            db.Attach(stored);
            var property = db.Entry(stored).Property(propertySelector);
            property.CurrentValue = read(kingData);
            property.IsModified = true;
            await db.SaveChangesAsync();
        }
        finally
        {
            Writes.Release();
        }

        logger.LogInformation("King property persisted for nation {Nation}", kingData.Nation);
    }

    public async Task BroadcastToNationAsync(AccountNation nation, Packet packet)
    {
        foreach (var session in sessionManager.GetAll())
        {
            if (session.Nation != nation)
                continue;

            try
            {
                await session.Client.SendPacket(packet);
            }
            catch
            {
                // Ignore broadcast failures per recipient.
            }
        }
    }
}
