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

    public bool IsKing(UserSession session, KingSystemData? kingData)
    {
        return kingData != null
            && string.Equals(kingData.KingName?.Trim(), session.Name, StringComparison.OrdinalIgnoreCase);
    }

    public async Task PersistKingPropertyAsync<TProperty>(
        KingSystemData kingData,
        Expression<Func<KingSystemData, TProperty>> propertySelector)
    {
        logger.LogInformation("King property persisted for nation {Nation}", kingData.Nation);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Attach(kingData);
        db.Entry(kingData).Property(propertySelector).IsModified = true;
        await db.SaveChangesAsync();
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
