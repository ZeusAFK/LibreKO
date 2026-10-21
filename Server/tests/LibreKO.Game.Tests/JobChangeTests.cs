using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
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
    private const short EarnedPoints = 3;
    private const byte TrainedStrength = 90;
    private const byte TrainedMastery = 4;
    private const byte SpentPoints = 2;
    private const int ClassChangeNpc = 99_001;
    private const int Blacksmith = 99_002;

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

    [Theory]
    [InlineData(CharacterDevelopmentPacketCoordinator.GrandMerchantNpcId, 0)]
    [InlineData(ClassChangeNpc, NpcData.TypeClassChange)]
    public async Task AJobChangeAtAClassChangeNpcGoesThrough(int npcId, byte npcType)
    {
        using var provider = Provider();
        var player = Player(provider);
        StandAt(provider, player, npcId, npcType);

        await provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>().HandleClassChangeAsync(player.Client, JobChangeRequest());

        player.Class.Should().NotBe(Warrior);
        Token(player).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AJobChangeAwayFromTheClassChangeNpcChangesNothing()
    {
        using var provider = Provider();
        var player = Player(provider);
        StandAt(provider, player, Blacksmith, NpcData.TypeRepairMerchant);

        await provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>().HandleClassChangeAsync(player.Client, JobChangeRequest());

        player.Class.Should().Be(Warrior, "only the NPCs that open the class-change window change jobs");
        Token(player).IsEmpty.Should().BeFalse();
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

    public enum Tie { Trading, Stunned, Fighting }

    [Theory]
    [InlineData(Tie.Trading)]
    [InlineData(Tie.Stunned)]
    [InlineData(Tie.Fighting)]
    public async Task APlayerTiedUpWhileTheChangeWaitsKeepsTheJobAndTheToken(Tie tie)
    {
        UserSession? player = null;
        var meanwhile = new DuringWarehouseRead
        {
            Meanwhile = () =>
            {
                switch (tie)
                {
                    case Tie.Trading:
                        player!.Trade.ExchangeUser = PlayerId + 1;
                        break;
                    case Tie.Stunned:
                        player!.SetBuff(StunRow, new ActiveBuff
                        {
                            MagicId = StunRow, CasterId = 1, BuffType = BuffType.Stun, Duration = 2, ExpireTicks = DateTime.UtcNow.AddSeconds(2).Ticks,
                        });
                        break;
                    case Tie.Fighting:
                        player!.MarkCombat();
                        break;
                }
            },
        };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(meanwhile)));
        player = Player(provider);

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior);
        Token(player).ItemId.Should().Be(InventoryConstants.ItemJobChange);
    }

    [Fact]
    public async Task ATokenThatLeavesTheBagMidChangeChangesNothing()
    {
        UserSession? player = null;
        var sale = new DuringWarehouseRead { Meanwhile = () => Token(player!).Clear() };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(sale)));
        player = Player(provider);

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior, "the token went elsewhere, so it paid for no job");
        (await StoredAsync(provider)).Class.Should().Be(Warrior);
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
    public async Task PointsEarnedWhileAFailedJobChangeIsSavedAreKept()
    {
        UserSession? player = null;
        var earned = new DuringSave { Meanwhile = () => player!.StatPoints += EarnedPoints };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(earned, new ClassFaults())));
        player = Player(provider);
        var points = player.StatPoints;

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior);
        player.StatPoints.Should().Be((short)(points + EarnedPoints), "the undo takes back what the change gave, not what came after it");
    }

    [Fact]
    public async Task StatPointsSpentWhileAFailedJobChangeIsSavedComeBackOutOfTheStats()
    {
        UserSession? player = null;
        var spent = new DuringSave
        {
            Meanwhile = () =>
            {
                player!.StatPoints -= SpentPoints;
                player.Magic += SpentPoints;
            },
        };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(spent, new ClassFaults())));
        player = Player(provider);
        (player.Strength, player.StatPoints) = (TrainedStrength, 0);
        var magic = player.Magic;

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior);
        player.Magic.Should().Be(magic, "the points spent came from the change, so the magic they bought goes with it");
        player.Strength.Should().Be(TrainedStrength, "only a stat raised since pays the points back");
        player.StatPoints.Should().Be(0);
    }

    [Fact]
    public async Task MasteryPointsSpentWhileAFailedJobChangeIsSavedComeBackOutOfTheTree()
    {
        UserSession? player = null;
        const int tree = ProgressionTable.MasteryClassFirstSlot;
        var spent = new DuringSave
        {
            Meanwhile = () =>
            {
                player!.SkillPoints[ProgressionTable.MasteryPoolSlot] -= SpentPoints;
                player.SkillPoints[tree] += SpentPoints;
            },
        };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(spent, new ClassFaults())));
        player = Player(provider);
        player.SkillPoints[tree] = TrainedMastery;

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior);
        player.SkillPoints[tree].Should().Be(TrainedMastery, "the points spent came from the change, so the mastery they bought goes with it");
        player.SkillPoints[ProgressionTable.MasteryPoolSlot].Should().Be(0);
    }

    [Fact]
    public async Task ARetryingStoreCanTellAWrittenSaveFromALostOne()
    {
        LostAnswer.Answers.Clear();
        LostAnswer.LoseNextAnswer = true;
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options
            .ReplaceService<IExecutionStrategyFactory, LostAnswerFactory>()
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))));
        var player = Player(provider);
        await StoreBagAsync(provider, player);

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        LostAnswer.Answers.Should().Equal([true],
            "a save whose answer is lost is checked against the store before it is run again, and this one was written");
        (await StoredAsync(provider)).Class.Should().NotBe(Warrior);
        player.Class.Should().NotBe(Warrior);
    }

    [Fact]
    public async Task ARetryingStoreRunsAgainACommitLostBeforeItWasWritten()
    {
        LostAnswer.Answers.Clear();
        LostAnswer.LoseNextAnswer = true;
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options
            .ReplaceService<IExecutionStrategyFactory, LostAnswerFactory>()
            .AddInterceptors(new FirstClassSaveLost())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))));
        var player = Player(provider);
        await StoreBagAsync(provider, player);

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        LostAnswer.Answers.Should().Equal([false], "a commit whose answer was lost is read back, and this one never reached the store");
        (await StoredAsync(provider)).Class.Should().NotBe(Warrior, "the second run wrote it");
        player.Class.Should().NotBe(Warrior);
    }

    private sealed class FirstClassSaveLost : SaveChangesInterceptor
    {
        private int lost;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context?.ChangeTracker.Entries<Character>().Any(entry => entry.Property(nameof(Character.Class)).IsModified) == true
                && Interlocked.Exchange(ref lost, 1) == 0
                ? ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1))
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // Asks whether a save was written only after a failed attempt, as EF does.
    private sealed class LostAnswer(DbContext context) : IExecutionStrategy
    {
        public static System.Collections.Concurrent.ConcurrentQueue<bool> Answers { get; } = new();
        public static bool LoseNextAnswer { get; set; }

        public bool RetriesOnFailure => true;

        public TResult Execute<TState, TResult>(
            TState state, Func<DbContext, TState, TResult> operation, Func<DbContext, TState, ExecutionResult<TResult>>? verifySucceeded) =>
            operation(context, state);

        public async Task<TResult> ExecuteAsync<TState, TResult>(
            TState state,
            Func<DbContext, TState, CancellationToken, Task<TResult>> operation,
            Func<DbContext, TState, CancellationToken, Task<ExecutionResult<TResult>>>? verifySucceeded,
            CancellationToken cancellationToken = default)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var result = await operation(context, state, cancellationToken);
                    if (LoseNextAnswer && verifySucceeded != null)
                    {
                        LoseNextAnswer = false;
                        throw new TimeoutException("the answer to the commit was lost");
                    }

                    return result;
                }
                catch (Exception) when (attempt == 0 && verifySucceeded != null)
                {
                    var verdict = await verifySucceeded(context, state, cancellationToken);
                    Answers.Enqueue(verdict.IsSuccessful);
                    if (verdict.IsSuccessful)
                        return verdict.Result;
                }
            }
        }
    }

    private sealed class LostAnswerFactory(ICurrentDbContext current) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new LostAnswer(current.Context);
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

    [Fact]
    public async Task PointsEarnedWhileAFailedJobChangeWaitsAreKept()
    {
        UserSession? player = null;
        var earned = new DuringWarehouseRead { Meanwhile = () => player!.StatPoints += EarnedPoints };
        using var provider = Provider(services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(earned, new ClassFaults())));
        player = Player(provider);
        var points = player.StatPoints;

        await provider.GetRequiredService<IJobChangeService>().HandlePromoteNoviceAsync(player, Master, Rogue);

        player.Class.Should().Be(Warrior);
        player.StatPoints.Should().Be((short)(points + EarnedPoints), "a failed change puts back the job as it found it, not as it was asked for");
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

    private static Packet JobChangeRequest()
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.JobChangeResult);
        packet.WriteByte(Master);
        packet.WriteByte(Rogue);
        packet.ResetOffset();
        return packet;
    }

    private static async Task StoreBagAsync(ServiceProvider provider, UserSession player)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Characters.SingleAsync(c => c.Id == PlayerId);
        stored.Items = UserSessionBinaryState.SerializeItems(player.Inventory);
        await db.SaveChangesAsync();
    }

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
