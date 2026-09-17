using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public sealed record AccountOccupant(
    int ServerId,
    string ServerName,
    string CharacterName,
    AccountClaimStage Stage);

public sealed record AccountLockResult(bool Granted, AccountOccupant? Occupant);

public interface IAccountLockService
{
    Task<AccountLockResult> AcquireAsync(IClient client, int accountId);
    Task<AccountKickCode> KickAsync(int accountId);
    bool Owns(IClient client);
    Task ReleaseAsync(IClient client);
    Task ClearOwnClaimsAsync();
}

public sealed class AccountClaim(int accountId, IClient client)
{
    public int AccountId { get; } = accountId;
    public IClient Client { get; } = client;
}

public class AccountLockService(
    IServiceScopeFactory scopeFactory,
    IServerRepository serverRepository,
    ISessionTerminationService sessionTerminationService,
    SessionManager sessionManager,
    IOptions<GameServerSettings> settings,
    ILogger<AccountLockService> logger) : IAccountLockService
{
    private const int EvictionGraceMs = 500;

    private readonly ConcurrentDictionary<int, AccountClaim> _claims = new();
    private readonly ConcurrentDictionary<Guid, int> _accountByClient = new();

    public async Task<AccountLockResult> AcquireAsync(IClient client, int accountId)
    {
        var wanted = new AccountClaim(accountId, client);

        while (true)
        {
            var current = _claims.GetOrAdd(accountId, wanted);
            if (ReferenceEquals(current, wanted) || current.Client.Id == client.Id)
                break;

            if (current.Client.IsConnected)
                return new AccountLockResult(false, await DescribeAsync(current));

            if (!_claims.TryUpdate(accountId, wanted, current))
                continue;

            logger.LogInformation(
                "Account {AccountId}: replacing dropped claim from client {ClientId}",
                accountId, current.Client.Id);
            _accountByClient.TryRemove(new KeyValuePair<Guid, int>(current.Client.Id, accountId));
            break;
        }

        _accountByClient[client.Id] = accountId;
        await SetOnlineAsync(accountId);
        return new AccountLockResult(true, null);
    }

    public async Task<AccountKickCode> KickAsync(int accountId)
    {
        await ClearOnlineAsync(accountId);

        if (!_claims.TryGetValue(accountId, out var claim))
            return AccountKickCode.NotOnline;

        await EvictAsync(claim);
        return AccountKickCode.Done;
    }

    public bool Owns(IClient client)
        => _accountByClient.TryGetValue(client.Id, out var accountId)
           && _claims.TryGetValue(accountId, out var claim)
           && claim.Client.Id == client.Id;

    public async Task ReleaseAsync(IClient client)
    {
        if (!_accountByClient.TryRemove(client.Id, out var accountId))
            return;

        if (!_claims.TryGetValue(accountId, out var claim) || claim.Client.Id != client.Id)
            return;

        _claims.TryRemove(new KeyValuePair<int, AccountClaim>(accountId, claim));
        await ClearOnlineAsync(accountId);
    }

    public async Task ClearOwnClaimsAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            var cleared = await accounts.ClearOnlineServerForServerAsync(settings.Value.ServerId);
            if (cleared > 0)
                logger.LogInformation("Cleared {Count} stale account claim(s) left by a previous run", cleared);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clear stale account claims for server {ServerId}", settings.Value.ServerId);
        }
    }

    private async Task EvictAsync(AccountClaim claim)
    {
        _claims.TryRemove(new KeyValuePair<int, AccountClaim>(claim.AccountId, claim));
        _accountByClient.TryRemove(new KeyValuePair<Guid, int>(claim.Client.Id, claim.AccountId));

        logger.LogInformation(
            "Evicting account {AccountId} (client {ClientId}) for a takeover",
            claim.AccountId, claim.Client.Id);

        var notice = SessionPacketWriter.KickResult(AccountKickCode.Evicted);
        await claim.Client.SendPacket(notice);

        try
        {
            await sessionTerminationService.LogoutAsync(claim.Client);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error terminating evicted session for account {AccountId}", claim.AccountId);
        }

        var evicted = claim.Client;
        _ = Task.Run(async () =>
        {
            await Task.Delay(EvictionGraceMs);
            evicted.Disconnect();
        });
    }

    private async Task<AccountOccupant> DescribeAsync(AccountClaim claim)
    {
        var serverId = settings.Value.ServerId;
        var name = string.Empty;
        try
        {
            var servers = await serverRepository.GetServers();
            name = servers.FirstOrDefault(s => s.Id == serverId)?.Name ?? string.Empty;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not resolve this server's name for an occupancy reply");
        }

        var session = sessionManager.GetByClientId(claim.Client.Id);
        return new AccountOccupant(
            serverId,
            name,
            session?.Name ?? string.Empty,
            session != null ? AccountClaimStage.InGame : AccountClaimStage.PreGame);
    }

    private async Task SetOnlineAsync(int accountId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            await accounts.SetOnlineServerAsync(accountId, settings.Value.ServerId);
        }
        catch (ObjectDisposedException)
        {
            logger.LogDebug("Account {AccountId} came online as the host was shutting down", accountId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to mark account {AccountId} online", accountId);
        }
    }

    private async Task ClearOnlineAsync(int accountId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            await accounts.ClearOnlineServerAsync(accountId);
        }
        catch (ObjectDisposedException)
        {
            logger.LogDebug(
                "Account {AccountId} disconnected after the host shut down; the bulk claim clear covers it",
                accountId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clear the online flag for account {AccountId}", accountId);
        }
    }
}
