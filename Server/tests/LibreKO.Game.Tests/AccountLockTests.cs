using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AccountLockTests : GameTestBase
{
    private const int AccountId = 4711;
    private const int ServerId = 1;

    [Fact]
    public async Task AcquireAsync_RefusesASecondLiveConnectionForTheSameAccount()
    {
        var (service, _, _) = CreateService();
        var first = CreateClient(connected: true);
        var second = CreateClient(connected: true);

        (await service.AcquireAsync(first, AccountId)).Granted.Should().BeTrue();

        var refused = await service.AcquireAsync(second, AccountId);
        refused.Granted.Should().BeFalse();
        refused.Occupant.Should().NotBeNull();
        refused.Occupant!.Stage.Should().Be(AccountClaimStage.PreGame);
        service.Owns(second).Should().BeFalse();
        service.Owns(first).Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_ReportsTheCharacterOfAnInGameOccupant()
    {
        var (service, _, provider) = CreateService();
        var first = CreateClient(connected: true);
        await service.AcquireAsync(first, AccountId);

        var sessions = provider.GetRequiredService<SessionManager>();
        sessions.CreateSession(first, characterId: 90210, accountId: AccountId).Name = "Aurelia";

        var refused = await service.AcquireAsync(CreateClient(connected: true), AccountId);

        refused.Granted.Should().BeFalse();
        refused.Occupant!.Stage.Should().Be(AccountClaimStage.InGame);
        refused.Occupant.CharacterName.Should().Be("Aurelia");
    }

    [Fact]
    public async Task AcquireAsync_TakesOverAClaimWhoseConnectionIsGone()
    {
        var (service, _, _) = CreateService();
        var dropped = CreateClient(connected: false);
        var reconnecting = CreateClient(connected: true);

        await service.AcquireAsync(dropped, AccountId);

        (await service.AcquireAsync(reconnecting, AccountId)).Granted.Should().BeTrue();
        service.Owns(reconnecting).Should().BeTrue();
        service.Owns(dropped).Should().BeFalse();
    }

    [Fact]
    public async Task KickAsync_EvictsTheHolderAndFreesTheAccount()
    {
        var (service, termination, _) = CreateService();
        var holder = CreateClient(connected: true);
        await service.AcquireAsync(holder, AccountId);

        (await service.KickAsync(AccountId)).Should().Be(AccountKickCode.Done);

        await holder.Received(1).SendPacket(
            Arg.Is<Packet>(p => p.GetOpcode() == (byte)GameOpcodes.GS_KICKOUT),
            Arg.Any<CancellationToken>());
        await termination.Received(1).LogoutAsync(holder, Arg.Any<CancellationToken>());
        service.Owns(holder).Should().BeFalse();

        (await service.AcquireAsync(CreateClient(connected: true), AccountId)).Granted.Should().BeTrue();
    }

    [Fact]
    public async Task KickAsync_ReportsNotOnlineWhenNobodyHoldsTheAccount()
    {
        var (service, _, _) = CreateService();

        (await service.KickAsync(AccountId)).Should().Be(AccountKickCode.NotOnline);
    }

    [Fact]
    public async Task ReleaseAsync_FreesTheAccountForTheNextConnection()
    {
        var (service, _, _) = CreateService();
        var holder = CreateClient(connected: true);
        await service.AcquireAsync(holder, AccountId);

        await service.ReleaseAsync(holder);

        service.Owns(holder).Should().BeFalse();
        (await service.AcquireAsync(CreateClient(connected: true), AccountId)).Granted.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseAsync_OfAnEvictedClientLeavesTheNewHolderAlone()
    {
        var (service, _, _) = CreateService();
        var first = CreateClient(connected: false);
        var second = CreateClient(connected: true);

        await service.AcquireAsync(first, AccountId);
        await service.AcquireAsync(second, AccountId);
        await service.ReleaseAsync(first);

        service.Owns(second).Should().BeTrue();
    }

    [Fact]
    public async Task AcquireAsync_MirrorsTheClaimOnTheAccountRow()
    {
        var (service, _, provider) = CreateService();
        var holder = CreateClient(connected: true);

        await service.AcquireAsync(holder, AccountId);
        (await ReadOnlineServerAsync(provider)).Should().Be(ServerId);

        await service.ReleaseAsync(holder);
        (await ReadOnlineServerAsync(provider)).Should().BeNull();
    }

    [Fact]
    public async Task ClearOwnClaimsAsync_ClearsRowsLeftBehindByAPreviousRun()
    {
        var (service, _, provider) = CreateService();
        await using (var scope = provider.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
            await accounts.SetOnlineServerAsync(AccountId, ServerId);
        }

        await service.ClearOwnClaimsAsync();

        (await ReadOnlineServerAsync(provider)).Should().BeNull();
    }

    private static async Task<int?> ReadOnlineServerAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.Accounts.FindAsync(AccountId);
        return account?.OnlineServerId;
    }

    private static (AccountLockService Service, ISessionTerminationService Termination, ServiceProvider Provider) CreateService()
    {
        var provider = CreateProvider(db => db.Accounts.Add(new LibreKO.Common.Domain.Entities.Account
        {
            Id = AccountId,
            Login = "claimant",
            Password = "hash",
        }));

        var termination = Substitute.For<ISessionTerminationService>();
        var servers = Substitute.For<IServerRepository>();
        servers.GetServers().Returns(Task.FromResult(new List<LibreKO.Common.Domain.Entities.Server>
        {
            new() { Id = ServerId, Name = "Beramus Legacy", IpAddress = "127.0.0.1", LanIpAddress = "127.0.0.1", Port = 15001 },
        }));

        var service = new AccountLockService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            servers,
            termination,
            provider.GetRequiredService<SessionManager>(),
            Options.Create(new GameServerSettings { ServerId = ServerId }),
            NullLogger<AccountLockService>.Instance);

        return (service, termination, provider);
    }

    private static IClient CreateClient(bool connected)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.IsConnected.Returns(connected);
        return client;
    }
}
