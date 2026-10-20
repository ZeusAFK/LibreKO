using FluentAssertions;
using LibreKO.Common.Domain.Entities;
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

public class JobChangeTests : GameTestBase
{
    private const int PlayerId = 620;
    private const short Warrior = 101;
    private const byte KarusBig = 1;
    private const byte Rogue = (byte)JobChangeTarget.Rogue;
    private const byte Master = 0;
    private const byte Level = 10;
    private const short PlayerHp = 100;
    private const int StunRow = 110560;

    [Fact]
    public async Task AJobChangeSpendsTheTokenAndSavesTheClassAndRaceTogether()
    {
        using var provider = Provider();
        var player = Player(provider);
        var (newClass, newRace) = JobChangeRules.Resolve(Warrior, KarusBig, (byte)AccountNation.Karus, Rogue, Master)!.Value;

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(newClass);
        Token(player).IsEmpty.Should().BeTrue();
        var stored = await StoredAsync(provider);
        stored.Class.Should().Be(newClass);
        stored.Race.Should().Be(newRace);
        stored.Items.Should().Equal(player.SerializeItems(), "the token and the new job are saved together");
    }

    [Fact]
    public async Task ADeadPlayerIsNotRevivedByAJobChange()
    {
        using var provider = Provider();
        var player = Player(provider);
        player.Hp = 0;

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Hp.Should().Be(0, "a job change refills HP, so the dead cannot use it");
        player.Class.Should().Be(Warrior);
        Token(player).ItemId.Should().Be(InventoryConstants.ItemJobChange);
    }

    [Fact]
    public async Task AFighterCannotChangeJob()
    {
        using var provider = Provider();
        var player = Player(provider);
        player.MarkCombat();

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior, "a job change refills HP and MP, which is no way out of a fight");
        Token(player).ItemId.Should().Be(InventoryConstants.ItemJobChange);
    }

    [Fact]
    public async Task APlayerWhoDiesDuringTheSaveIsNotRevived()
    {
        UserSession? player = null;
        var blow = new DuringWarehouseRead { Meanwhile = () => player!.Hp = 0 };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(blow)));
        player = Player(provider);

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Hp.Should().Be(0, "a job change refills only the living");
        player.Class.Should().Be(Warrior);
        Token(player).ItemId.Should().Be(InventoryConstants.ItemJobChange);
    }

    [Fact]
    public async Task APlayerKilledWhileTheJobIsSavedStaysDead()
    {
        UserSession? player = null;
        var blow = new DuringSave { Meanwhile = () => player!.Hp = 0 };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(blow)));
        player = Player(provider);

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().NotBe(Warrior, "the job was saved");
        player.Hp.Should().Be(0, "a job change refills only the living");
    }

    private sealed class DuringSave : SaveChangesInterceptor
    {
        public Action Meanwhile { get; init; } = () => { };

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Meanwhile();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task AHeldPlayerCannotChangeJob()
    {
        using var provider = Provider();
        var player = Player(provider);
        player.SetBuff(StunRow, new ActiveBuff
        {
            MagicId = StunRow, CasterId = 1, BuffType = BuffType.Stun, Duration = 2, ExpireTicks = DateTime.UtcNow.AddSeconds(2).Ticks,
        });

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior);
        Token(player).ItemId.Should().Be(InventoryConstants.ItemJobChange);
    }

    private sealed class DuringWarehouseRead : Microsoft.EntityFrameworkCore.Diagnostics.IQueryExpressionInterceptor
    {
        public Action Meanwhile { get; init; } = () => { };

        public System.Linq.Expressions.Expression QueryCompilationStarting(
            System.Linq.Expressions.Expression queryExpression, QueryExpressionEventData eventData)
        {
            if (queryExpression is System.Linq.Expressions.MethodCallExpression { Arguments: [Microsoft.EntityFrameworkCore.Query.EntityQueryRootExpression root, ..] }
                && root.EntityType.ClrType == typeof(Warehouse))
                Meanwhile();
            return queryExpression;
        }
    }

    [Fact]
    public async Task AJobChangeWhoseSaveFailsKeepsTheTokenAndTheJob()
    {
        var faults = new ClassFaults();
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var player = Player(provider);
        var strength = player.Strength;

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior);
        player.Race.Should().Be(KarusBig);
        player.Strength.Should().Be(strength);
        Token(player).ItemId.Should().Be(InventoryConstants.ItemJobChange);
        (await StoredAsync(provider)).Class.Should().Be(Warrior);
    }

    private static ServiceProvider Provider(Action<IServiceCollection>? configure = null) => CreateProvider(db =>
    {
        db.Characters.Add(new Character
        {
            Id = PlayerId, AccountId = PlayerId, Name = "Climber", Class = Warrior, Race = KarusBig, Level = Level,
            Items = new byte[InventoryConstants.InventoryTotal * 8], SkillPointData = new byte[9],
        });
        db.SaveChanges();
    }, gameData => gameData.GetItem(InventoryConstants.ItemJobChange).Returns(new ItemData { Num = InventoryConstants.ItemJobChange, Duration = 1 }),
        configureServices: configure);

    private static UserSession Player(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, PlayerId, PlayerId);
        session.Name = "Climber";
        session.Nation = AccountNation.Karus;
        session.Class = Warrior;
        session.Race = KarusBig;
        session.Level = Level;
        session.Hp = session.MaxHp = PlayerHp;
        var token = Token(session);
        token.ItemId = InventoryConstants.ItemJobChange;
        token.Count = 1;
        token.Durability = 1;
        return session;
    }

    private static ItemSlot Token(UserSession session) => session.Inventory[InventoryConstants.InventoryStart];

    private static async Task<Character> StoredAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == PlayerId);
    }

    private sealed class ClassFaults : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context?.ChangeTracker.Entries<Character>().Any(entry => entry.Property(nameof(Character.Class)).IsModified) == true
                ? throw new DbUpdateException("the database went away")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
