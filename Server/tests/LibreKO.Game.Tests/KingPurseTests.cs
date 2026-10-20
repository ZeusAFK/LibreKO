using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class KingPurseTests : GameTestBase
{
    private const string King = "Rex";
    private const string Winner = "Winner";
    private const int Treasury = 1_000;
    private const int Prize = 100;
    private const int Rounds = 200_000;
    private const int WritersAtOnce = 5;
    private const int SavesForAWriteAndOneWaiting = 2;
    private static readonly TimeSpan RivalWait = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task APrizeIsPaidFromTheTreasury()
    {
        var kingdom = new KingSystemData { Nation = (byte)AccountNation.Karus, KingName = King, NationalTreasury = Treasury };
        using var provider = Provider(kingdom);
        var (king, winner) = Players(provider, winnerPurse: 0);

        await Award(provider, king, Prize);

        winner.Money.Should().Be(Prize);
        kingdom.NationalTreasury.Should().Be(Treasury - Prize);
    }

    [Fact]
    public async Task APrizeThatWouldOverfillThePurseStaysInTheTreasury()
    {
        var kingdom = new KingSystemData { Nation = (byte)AccountNation.Karus, KingName = King, NationalTreasury = Treasury };
        using var provider = Provider(kingdom);
        var (king, winner) = Players(provider, winnerPurse: ExchangePacketConstants.CoinMax - Prize + 1);

        await Award(provider, king, Prize);

        winner.Money.Should().Be(ExchangePacketConstants.CoinMax - Prize + 1, "a purse never passes the coin cap");
        kingdom.NationalTreasury.Should().Be(Treasury, "the prize that found no room goes back");
    }

    [Fact]
    public void SpendsAndTaxesArrivingTogetherLoseNothing()
    {
        var kingdom = new KingSystemData { NationalTreasury = Rounds };

        Parallel.For(0, Rounds, _ =>
        {
            KingSystemRuntimeService.AddTax(kingdom, 1, 1);
            KingSystemRuntimeService.TrySpendTreasury(kingdom, 1).Should().BeTrue();
        });

        kingdom.NationalTreasury.Should().Be(Rounds);
        kingdom.TerritoryTax.Should().Be(Rounds);
    }

    [Fact]
    public async Task NationalPointsStopAtTheLargestTotalInsteadOfWrapping()
    {
        using var provider = Provider(new KingSystemData());
        var (_, winner) = Players(provider, winnerPurse: 0);
        winner.Loyalty = winner.MonthlyLoyalty = int.MaxValue - 1;

        await provider.GetRequiredService<ILoyaltyService>().ChangeAsync(winner, Prize);

        winner.Loyalty.Should().Be(int.MaxValue);
        winner.MonthlyLoyalty.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task ATreasuryWriteStartedLaterIsStoredLast()
    {
        var kingdom = new KingSystemData { Nation = (byte)AccountNation.Karus, KingName = King, NationalTreasury = Treasury };
        var saves = new FirstSaveWaits();
        using var provider = Provider(kingdom, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(saves)));
        var runtime = provider.GetRequiredService<IKingSystemRuntimeService>();
        saves.Meanwhile = () =>
        {
            KingSystemRuntimeService.TrySpendTreasury(kingdom, Prize);
            return runtime.PersistKingPropertyAsync(kingdom, entry => entry.NationalTreasury);
        };

        await runtime.PersistKingPropertyAsync(kingdom, entry => entry.NationalTreasury);
        await saves.Rival;

        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().KingSystem.AsNoTracking().SingleAsync())
            .NationalTreasury.Should().Be(Treasury - Prize, "a spend stored after a tax is not overwritten by the older value");
    }

    [Fact]
    public async Task TreasuryWritesWaitingTogetherAreStoredOnce()
    {
        var kingdom = new KingSystemData { Nation = (byte)AccountNation.Karus, KingName = King, NationalTreasury = Treasury };
        var saves = new FirstSaveWaits();
        using var provider = Provider(kingdom, services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(saves)));
        var runtime = provider.GetRequiredService<IKingSystemRuntimeService>();
        saves.Meanwhile = () =>
        {
            KingSystemRuntimeService.TrySpendTreasury(kingdom, Prize);
            return Task.WhenAll(Enumerable.Range(0, WritersAtOnce).Select(_ => runtime.PersistKingPropertyAsync(kingdom, entry => entry.NationalTreasury)));
        };

        await runtime.PersistKingPropertyAsync(kingdom, entry => entry.NationalTreasury);
        await saves.Rival;

        saves.Saves.Should().Be(SavesForAWriteAndOneWaiting, "writes queued behind one another take in the newest value once");
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().KingSystem.AsNoTracking().SingleAsync())
            .NationalTreasury.Should().Be(Treasury - Prize);
    }

    // The first save waits a while for a rival write started inside it, which can finish only if nothing holds it back.
    private sealed class FirstSaveWaits : SaveChangesInterceptor
    {
        private int saves;

        public Func<Task> Meanwhile { get; set; } = () => Task.CompletedTask;
        public Task Rival { get; private set; } = Task.CompletedTask;
        public int Saves => Volatile.Read(ref saves);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saves) == 1)
            {
                Rival = Task.Run(Meanwhile);
                await Task.WhenAny(Rival, Task.Delay(RivalWait, cancellationToken));
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static ServiceProvider Provider(KingSystemData kingdom, Action<IServiceCollection>? configure = null) => CreateProvider(db =>
        {
            db.KingSystem.Add(new KingSystemData { Nation = kingdom.Nation, KingName = kingdom.KingName, NationalTreasury = kingdom.NationalTreasury });
            db.SaveChanges();
        }, gameData =>
        gameData.KingSystemTable.Returns(new Dictionary<byte, KingSystemData> { [(byte)AccountNation.Karus] = kingdom }),
        configureServices: configure);

    private static (UserSession King, UserSession Winner) Players(ServiceProvider provider, int winnerPurse)
    {
        var sessions = provider.GetRequiredService<SessionManager>();
        UserSession Make(int id, string name)
        {
            var client = Substitute.For<IClient>();
            client.Id.Returns(Guid.NewGuid());
            client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            var session = sessions.CreateSession(client, id, id);
            session.Name = name;
            session.Nation = AccountNation.Karus;
            session.Hp = 100;
            return session;
        }

        var winner = Make(2, Winner);
        winner.Money = winnerPurse;
        return (Make(1, King), winner);
    }

    private static Task Award(ServiceProvider provider, UserSession king, int amount)
    {
        var packet = new Packet(GameOpcodes.GS_KING);
        packet.WriteByte(KingPacketConstants.EventPrize);
        packet.WriteSByteString(Winner);
        packet.WriteInt(amount);
        packet.ResetOffset();
        return provider.GetRequiredService<IKingGovernancePacketService>().HandleKingEventAsync(king, packet);
    }
}
