using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Data.Common;

namespace LibreKO.Game.Tests;

// SQLite, unlike the in-memory store, rolls a transaction back.
public class ClanCreationTests : GameTestBase, IDisposable
{
    private const int FounderId = 650;
    private const int RivalId = 651;
    private const short OtherClan = 77;
    private const string ClanName = "Ravens";
    private const string Renamed = "Crows";
    private const short HeldMembers = 2;
    private static readonly TimeSpan RivalWait = TimeSpan.FromMilliseconds(300);

    private readonly SqliteConnection database = new("DataSource=:memory:");

    public ClanCreationTests() => database.Open();

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task APlayerWithTheCoinsInATownFoundsAClan()
    {
        using var provider = Provider(new Hooks());
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        founder.KnightsId.Should().BeGreaterThan(0);
        founder.Money.Should().Be(0);
        (await ClansNamed(provider, ClanName)).Should().Be(1);
        var stored = await Stored(provider, FounderId);
        stored.KnightsId.Should().Be(founder.KnightsId, "the founder's clan is stored with the clan");
        stored.Money.Should().Be(0);
        stored.Fame.Should().Be(ClanRules.FameChief);
    }

    [Fact]
    public async Task TwoClansFoundedAtOnceUnderOneNameMakeOne()
    {
        var hooks = new Hooks();
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        var rival = Founder(provider, RivalId);
        Task? rivalFounding = null;
        var checks = 0;
        hooks.BeforeNameCheck = async () =>
        {
            if (Interlocked.Increment(ref checks) > 1)
                return;

            rivalFounding = Task.Run(() => Create(provider, rival, ClanName));
            await Task.WhenAny(rivalFounding, Task.Delay(RivalWait));
        };

        await Create(provider, founder, ClanName);
        await rivalFounding!;

        (await ClansNamed(provider, ClanName)).Should().Be(1, "a name is checked and taken one founding at a time");
        new[] { founder, rival }.Count(player => player.KnightsId > 0).Should().Be(1);
        new[] { founder, rival }.Sum(player => (long)player.Money).Should().Be(ClanRules.CreationCoins, "only the founder pays");
    }

    [Fact]
    public async Task CoinsSpentWhileTheClanIsFoundedAreNotTakenAgain()
    {
        var hooks = new Hooks();
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        hooks.BeforeNameCheck = () =>
        {
            founder.Money = 0;
            return Task.CompletedTask;
        };

        await Create(provider, founder, ClanName);

        founder.Money.Should().Be(0, "the purse never goes below empty");
        founder.KnightsId.Should().Be(0);
        (await ClansNamed(provider, ClanName)).Should().Be(0);
        Answered(founder, KnightsCreateResult.NotEnoughCoins);
    }

    [Fact]
    public async Task AFounderWhoJoinedAnotherClanMeanwhileKeepsItAndTheCoins()
    {
        var hooks = new Hooks();
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        hooks.AfterClanSaved = () => founder.KnightsId = OtherClan;

        await Create(provider, founder, ClanName);

        founder.KnightsId.Should().Be(OtherClan);
        founder.Money.Should().Be(ClanRules.CreationCoins);
        (await ClansNamed(provider, ClanName)).Should().Be(0, "the clan saved for the founding is rolled back");
        Answered(founder, KnightsCreateResult.AlreadyInClan);
    }

    [Fact]
    public async Task AClanThatCannotBeStoredCostsNothing()
    {
        var hooks = new Hooks { FailClanSave = true };
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        founder.Money.Should().Be(ClanRules.CreationCoins);
        founder.KnightsId.Should().Be(0);
        (await ClansNamed(provider, ClanName)).Should().Be(0);
        Answered(founder, KnightsCreateResult.TryAgainLater);
    }

    [Fact]
    public async Task AFoundingWhoseFounderCannotBeStoredLeavesNoClan()
    {
        var hooks = new Hooks { FailFounderSave = true };
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        founder.Fame = Captain;

        await Create(provider, founder, ClanName);

        (await ClansNamed(provider, ClanName)).Should().Be(0, "the clan row commits with the founder or not at all");
        founder.Money.Should().Be(ClanRules.CreationCoins);
        founder.KnightsId.Should().Be(0);
        founder.KnightsName.Should().BeEmpty();
        founder.KnightsFame.Should().Be(0);
        founder.Fame.Should().Be(Captain);
        provider.GetRequiredService<SessionManager>().Knights.GetAll().Should().BeEmpty();
        var stored = await Stored(provider, FounderId);
        stored.KnightsId.Should().Be(0);
        stored.Money.Should().Be(ClanRules.CreationCoins);
        Answered(founder, KnightsCreateResult.TryAgainLater);
    }

    [Fact]
    public async Task AFoundingWrittenButReportedAsFailedStands()
    {
        var hooks = new Hooks { LoseCommitAnswer = true };
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        (await ClansNamed(provider, ClanName)).Should().Be(1);
        founder.KnightsId.Should().BeGreaterThan(0, "undoing a written founding would leave a clan without its chief");
        founder.Money.Should().Be(0);
        (await Stored(provider, FounderId)).KnightsId.Should().Be(founder.KnightsId);
    }

    [Fact]
    public async Task AFoundingWhoseCommitNeverLandsLeavesNothing()
    {
        var hooks = new Hooks { FailCommit = true };
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        (await ClansNamed(provider, ClanName)).Should().Be(0);
        founder.KnightsId.Should().Be(0, "the check for a lost answer looks only at what was committed");
        founder.Money.Should().Be(ClanRules.CreationCoins);
        (await Stored(provider, FounderId)).KnightsId.Should().Be(0);
        Answered(founder, KnightsCreateResult.TryAgainLater);
    }

    [Fact]
    public async Task AStoreThatRetriesFindsALostFoundingThroughABriefOutage()
    {
        var hooks = new Hooks { LoseCommitAnswer = true, FailReadAfterLostAnswer = true };
        using var provider = Provider(hooks, retrying: true);
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        (await ClansNamed(provider, ClanName)).Should().Be(1);
        founder.KnightsId.Should().BeGreaterThan(0, "the check is retried like any other read, so a founding that landed stands");
        (await Stored(provider, FounderId)).KnightsId.Should().Be(founder.KnightsId);
    }

    [Fact]
    public async Task AStoreThatRetriesStartsTheTransactionAgainWithoutASecondWarehouse()
    {
        var hooks = new Hooks { FailFirstStart = true };
        using var provider = Provider(hooks, retrying: true);
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        (await ClansNamed(provider, ClanName)).Should().Be(1);
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Warehouses.CountAsync(w => w.AccountId == FounderId)).Should().Be(1);
    }

    [Fact]
    public async Task FoundingClearsAMemoLeftFromAnOldClan()
    {
        using var provider = Provider(new Hooks());
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Characters.SingleAsync(c => c.Id == FounderId)).ClanMemo = "old clan";
            await db.SaveChangesAsync();
        }
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        (await Stored(provider, FounderId)).ClanMemo.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenThePurseLooksUntouchedTheClanItselfShowsWhetherTheFoundingLanded(bool landed)
    {
        var hooks = new Hooks();
        using var provider = Provider(hooks);
        var founder = Founder(provider, FounderId);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.Characters.SingleAsync(c => c.Id == FounderId);
            (stored.Money, stored.Items) = (0, founder.SavedItems);
            db.Knights.Add(new KnightsEntity { Name = Renamed, Chief = "Elder", Members = 1 });
            await db.SaveChangesAsync();
        }
        (hooks.LoseCommitAnswer, hooks.FailCommit) = (landed, !landed);

        await Create(provider, founder, ClanName);

        (await ClansNamed(provider, ClanName)).Should().Be(landed ? 1 : 0);
        (founder.KnightsId > 0).Should().Be(landed, "coins earned since the last save leave the purse looking as stored after paying");
        (await Stored(provider, FounderId)).KnightsId.Should().Be(founder.KnightsId);
    }

    [Fact]
    public async Task AStoreThatRetriesFoundsTheClanInOneTransaction()
    {
        using var provider = Provider(new Hooks(), retrying: true);
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        (await ClansNamed(provider, ClanName)).Should().Be(1);
        (await Stored(provider, FounderId)).KnightsId.Should().Be(founder.KnightsId);
    }

    [Fact]
    public async Task AStoreThatRetriesDoesNotFoundTheClanTwiceAfterAFailure()
    {
        var hooks = new Hooks { FailFounderSave = true };
        using var provider = Provider(hooks, retrying: true);
        var founder = Founder(provider, FounderId);

        await Create(provider, founder, ClanName);

        hooks.ClanSaves.Should().Be(1, "a retry would pay and lead a second time");
        founder.Money.Should().Be(ClanRules.CreationCoins);
        founder.KnightsId.Should().Be(0);
        (await ClansNamed(provider, ClanName)).Should().Be(0);
    }

    [Fact]
    public async Task ASaveOfAClanHeldInMemoryKeepsTheNameARenameStored()
    {
        using var provider = Provider(new Hooks());
        var held = new KnightsEntity { Name = ClanName, Chief = "Founder", Members = 1 };
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().CreateAsync(held);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Knights.FindAsync(held.Id))!.Name = Renamed;
            await db.SaveChangesAsync();
        }

        held.Members = HeldMembers;
        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IKnightsRepository>().UpdateAsync(held);

        await using var check = provider.CreateAsyncScope();
        var stored = await check.ServiceProvider.GetRequiredService<AppDbContext>().Knights.AsNoTracking().SingleAsync(clan => clan.Id == held.Id);
        stored.Name.Should().Be(Renamed);
        stored.Members.Should().Be(HeldMembers);
    }

    private const byte Captain = 6;

    private sealed class Hooks : ISaveChangesInterceptor, IDbTransactionInterceptor, IDbCommandInterceptor
    {
        private bool savingClan;
        private bool failNextRead;

        public Func<Task> BeforeNameCheck { get; set; } = () => Task.CompletedTask;
        public Action AfterClanSaved { get; set; } = () => { };
        public bool FailClanSave { get; init; }
        public bool FailFounderSave { get; init; }
        public bool LoseCommitAnswer { get; set; }
        public bool FailCommit { get; set; }
        public bool FailFirstStart { get; set; }
        public bool FailReadAfterLostAnswer { get; init; }
        public int ClanSaves { get; private set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var tracker = eventData.Context!.ChangeTracker;
            savingClan = tracker.Entries<KnightsEntity>().Any(entry => entry.State == EntityState.Added);
            if (savingClan)
            {
                ClanSaves++;
                if (FailClanSave)
                    throw new DbUpdateException("the database went away");
            }
            else if (FailFounderSave && tracker.Entries<Character>().Any(entry => entry.State == EntityState.Modified))
            {
                throw new DbUpdateException("the database went away");
            }

            return ValueTask.FromResult(result);
        }

        public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (savingClan)
                AfterClanSaved();
            return ValueTask.FromResult(result);
        }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!LoseCommitAnswer)
                return Task.CompletedTask;

            failNextRead = FailReadAfterLostAnswer;
            throw new DbUpdateException("the answer to the commit was lost");
        }

        public ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            FailCommit ? throw new DbUpdateException("the database went away before the commit") : ValueTask.FromResult(result);

        public ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (!FailFirstStart)
                return ValueTask.FromResult(result);

            FailFirstStart = false;
            throw new DbUpdateException("the database blinked");
        }

        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!failNextRead || !command.CommandText.Contains("Characters"))
                return ValueTask.FromResult(result);

            failNextRead = false;
            throw new DbUpdateException("the database is still coming back");
        }
    }

    private sealed class HookedKnights(IKnightsRepository inner, Hooks hooks) : IKnightsRepository
    {
        public async Task<bool> IsNameTakenAsync(string name)
        {
            await hooks.BeforeNameCheck();
            return await inner.IsNameTakenAsync(name);
        }

        public Task CreateAsync(KnightsEntity clan) => inner.CreateAsync(clan);
        public Task<KnightsEntity?> FindAsync(short id) => inner.FindAsync(id);
        public Task RemoveAsync(KnightsEntity entity) => inner.RemoveAsync(entity);
        public Task UpdateAsync(KnightsEntity clan) => inner.UpdateAsync(clan);
        public Task<List<ClanMemberProjection>> GetMembersAsync(short knightsId) => inner.GetMembersAsync(knightsId);
        public Task<List<Character>> GetCharactersByClanAsync(short knightsId) => inner.GetCharactersByClanAsync(knightsId);
        public Task<List<ClanMemberLoyalty>> GetMemberLoyaltyAsync() => inner.GetMemberLoyaltyAsync();
        public Task SyncCharacterClanStateAsync(int characterId, short knightsId, byte fame, int? money = null, int? loyalty = null) =>
            inner.SyncCharacterClanStateAsync(characterId, knightsId, fame, money, loyalty);
        public Task SetClanMemoAsync(int characterId, string memo) => inner.SetClanMemoAsync(characterId, memo);
    }

    private sealed class Retrying(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 3, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => true;
    }

    private ServiceProvider Provider(Hooks hooks, bool retrying = false) => CreateProvider(
        db =>
        {
            db.Accounts.AddRange(
                new Account { Id = FounderId, Login = $"founder{FounderId}", Password = "-" },
                new Account { Id = RivalId, Login = $"founder{RivalId}", Password = "-" });
            db.Characters.AddRange(
                new Character { Id = FounderId, AccountId = FounderId, Name = $"Founder{FounderId}", Level = ClanRules.CreationLevel, Money = ClanRules.CreationCoins },
                new Character { Id = RivalId, AccountId = RivalId, Name = $"Founder{RivalId}", Level = ClanRules.CreationLevel, Money = ClanRules.CreationCoins });
            db.SaveChanges();
        },
        configureServices: services => services.AddScoped<IKnightsRepository>(scope =>
            new HookedKnights(new KnightsRepository(scope.GetRequiredService<AppDbContext>()), hooks)),
        useDatabase: options => options
            .UseSqlite(database, sqlite =>
            {
                if (retrying)
                    sqlite.ExecutionStrategy(dependencies => new Retrying(dependencies));
            })
            .AddInterceptors(hooks));

    private static UserSession Founder(ServiceProvider provider, int characterId)
    {
        var sessions = provider.GetRequiredService<SessionManager>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = sessions.CreateSession(client, characterId, characterId);
        session.Name = $"Founder{characterId}";
        session.Nation = AccountNation.Karus;
        session.Level = ClanRules.CreationLevel;
        session.Money = ClanRules.CreationCoins;
        session.ZoneId = (byte)ZoneId.Moradon;
        sessions.Regions.AddToRegion(session);
        return session;
    }

    private static Task Create(ServiceProvider provider, UserSession founder, string name)
    {
        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteString(name);
        packet.ResetOffset();
        return provider.GetRequiredService<IKnightsMembershipPacketService>().HandleCreateAsync(founder, packet);
    }

    private static void Answered(UserSession founder, KnightsCreateResult result)
    {
        var expected = KnightsPacketWriter.CreateResult(result).GetData();
        founder.Client.Received().SendPacket(Arg.Is<Packet>(packet => packet.GetData().SequenceEqual(expected)), Arg.Any<CancellationToken>());
    }

    private static async Task<int> ClansNamed(ServiceProvider provider, string name)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Knights.CountAsync(clan => clan.Name == name);
    }

    private static async Task<Character> Stored(ServiceProvider provider, int characterId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.AsNoTracking().SingleAsync(c => c.Id == characterId);
    }
}
