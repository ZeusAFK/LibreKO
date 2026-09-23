using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface ISessionTerminationService
{
    Task SaveAsync(UserSession session, CancellationToken cancellationToken = default);
    Task LogoutAsync(IClient client, CancellationToken cancellationToken = default);
    Task DisconnectAsync(IClient client, CancellationToken cancellationToken = default);
    Task EvictForTakeoverAsync(UserSession session);
}

public class SessionTerminationService(
    SessionManager sessionManager,
    ICharacterStatePersister characterStatePersister,
    IChallengePacketCoordinator challengePacketCoordinator,
    IEventSystemsPacketCoordinator eventSystemsPacketCoordinator,
    IExchangePacketCoordinator exchangePacketCoordinator,
    IMerchantPacketCoordinator merchantPacketCoordinator,
    IPartyPacketCoordinator partyPacketCoordinator,
    IWorldPacketCoordinator worldPacketCoordinator,
    InstanceRoomRegistry instanceRooms,
    ILogger<SessionTerminationService> logger) : ISessionTerminationService
{
    // Cap concurrent disconnect-time DB work so a mass disconnect (thousands of bots
    // dropping at once) can't exhaust the connection pool. Static = shared across all
    // terminations regardless of this service's DI lifetime.
    private const int MaxConcurrentDisconnectDbOps = 24;
    private static readonly SemaphoreSlim DisconnectDbGate = new(MaxConcurrentDisconnectDbOps);

    public async Task SaveAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        if (session.IsBot)
            return;

        await characterStatePersister.SaveAsync(session, cancellationToken);
    }

    public async Task LogoutAsync(IClient client, CancellationToken cancellationToken = default)
    {
        client.ExpectedClose = true;
        await TerminateAsync(client, unexpectedDisconnect: false, cancellationToken);
    }

    public async Task DisconnectAsync(IClient client, CancellationToken cancellationToken = default)
    {
        await TerminateAsync(client, unexpectedDisconnect: true, cancellationToken);
    }

    public async Task EvictForTakeoverAsync(UserSession session)
    {
        session.Client.ExpectedClose = true;
        await RemoveFromWorldAsync(session);

        try
        {
            await ReleaseWorldStateAsync(session);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error releasing world state for {Name}", session.Name);
        }

        sessionManager.RemoveSession(session);
    }

    private async Task TerminateAsync(IClient client, bool unexpectedDisconnect, CancellationToken cancellationToken)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
        {
            await MarkOfflineAsync(client.CharacterId, cancellationToken);
            client.CharacterId = 0;
            return;
        }

        if (session.IsBot)
        {
            await RemoveFromWorldAsync(session);
            try
            {
                await ReleaseWorldStateAsync(session);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error releasing world state for bot {Name}", session.Name);
            }
            finally
            {
                sessionManager.RemoveSession(session);
                client.CharacterId = 0;
            }
            return;
        }

        if (unexpectedDisconnect)
            logger.LogInformation("Client disconnected unexpectedly: {Name} (CharId={CharId})", session.Name, session.CharacterId);

        await RemoveFromWorldAsync(session);

        try
        {
            await CleanupAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error cleaning up session for {Name}", session.Name);
        }
        finally
        {
            sessionManager.RemoveSession(session);
            await MarkOfflineAsync(session.CharacterId, cancellationToken);
            client.CharacterId = 0;
        }
    }

    // Runs before cleanup: while the session sits in its region, visibility paths re-hand it out.
    private async Task RemoveFromWorldAsync(UserSession session)
    {
        session.MovePending = false;
        sessionManager.Regions.RemoveFromRegion(session);
        instanceRooms.Leave(session);

        try
        {
            await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.Out);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to announce the departure of {Name}", session.Name);
        }
    }

    private async Task CleanupAsync(UserSession session, CancellationToken cancellationToken)
    {
        await ReleaseWorldStateAsync(session);

        await DisconnectDbGate.WaitAsync(cancellationToken);
        try
        {
            await characterStatePersister.SaveAsync(session, cancellationToken);
        }
        finally
        {
            DisconnectDbGate.Release();
        }
    }

    private async Task ReleaseWorldStateAsync(UserSession session)
    {
        if (session.Trade.IsTrading)
            await exchangePacketCoordinator.CancelAsync(session);
        if (session.Trade.IsRequestingChallenge || session.Trade.IsChallengeRequested)
            await challengePacketCoordinator.CancelAsync(session);
        if (session.HasRival)
            await eventSystemsPacketCoordinator.RemoveRivalAsync(session);
        if (session.Trade.IsMerchanting || session.Trade.MerchantItems.Any(item => item != null && !item.IsEmpty))
            await merchantPacketCoordinator.HandleSessionEndedAsync(session);
        if (session.IsInParty)
            await partyPacketCoordinator.RemoveMemberAsync(session, (short)session.CharacterId);

        session.IsMining = false;
        session.IsFishing = false;
    }

    private async Task MarkOfflineAsync(int characterId, CancellationToken cancellationToken)
    {
        if (characterId <= 0)
            return;

        // A newer session may already own this character after a login takeover —
        // don't flag the live character offline when the old connection cleans up.
        if (sessionManager.GetByCharacterId(characterId) != null)
            return;

        try
        {
            await DisconnectDbGate.WaitAsync(cancellationToken);
            try
            {
                await characterStatePersister.SetOnlineStateAsync(characterId, isOnline: false, cancellationToken);
            }
            finally
            {
                DisconnectDbGate.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to mark character {CharId} offline", characterId);
        }
    }
}
