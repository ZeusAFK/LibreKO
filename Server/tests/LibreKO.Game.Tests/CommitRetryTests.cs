using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
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

// SQLite fails a real command where the in-memory store has none to fail.
public class CommitRetryTests : GameTestBase, IDisposable
{
    private const int AliceId = 1;
    private const int BobId = 2;
    private const int Apple = 810418000;
    private const int Purse = 1_000;
    private const int Price = 100;
    private const string Subject = "Apples";
    private const string Body = "Enjoy them.";

    private readonly SqliteConnection database = new("DataSource=:memory:");

    public CommitRetryTests() => database.Open();

    public void Dispose() => database.Dispose();

    [Fact]
    public async Task ALostAnswerWhoseFirstCheckCannotReadIsNotSavedTwice()
    {
        var faults = new Faults();
        using var provider = Provider(faults, retrying: true);
        var alice = Online(provider, AliceId, "Alice");
        alice.Money = Purse;
        (faults.LoseNextCommitAnswer, faults.FailReadAfterLostAnswer) = (true, true);

        var outcome = await provider.GetRequiredService<ICharacterStatePersister>().CommitAsync(alice,
            db =>
            {
                alice.WithLock(s => s.Money -= Price);
                db.Mails.Add(new Mail
                {
                    Kind = MailKind.Player, SenderCharacterId = AliceId, SenderName = "Alice", RecipientCharacterId = BobId,
                    Subject = Subject, Body = Body, SentAt = DateTime.UtcNow,
                });
                return Task.FromResult(true);
            },
            () => alice.WithLock(s => s.Money += Price));

        outcome.Should().Be(StateCommit.Saved);
        (await MailsAsync(provider)).Should().Be(1, "the store tries the check again, not the save");
        alice.Money.Should().Be(Purse - Price);
    }

    [Fact]
    public async Task AMailWhoseTwinCountFailsIsNotTakenForSent()
    {
        var faults = new Faults();
        using var provider = Provider(faults, retrying: false);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Mails.Add(new Mail
            {
                Kind = MailKind.Player, SenderCharacterId = AliceId, SenderName = "Alice", RecipientCharacterId = BobId,
                Subject = Subject, Body = Body, SentAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var alice = Online(provider, AliceId, "Alice");
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Characters.SingleAsync(c => c.Id == AliceId)).Items = alice.SavedItems;
            await db.SaveChangesAsync();
        }
        var apples = alice.Inventory[InventoryConstants.InventoryStart];
        (apples.ItemId, apples.Count) = (Apple, 3);
        faults.FailNextCount = true;

        await provider.GetRequiredService<IMailService>().SendAsync(alice, "Bob", Subject, Body, 0,
            [new MailItemPick((byte)InventoryConstants.InventoryStart, 3)]);

        (await MailsAsync(provider)).Should().Be(1);
        alice.Inventory.Where(slot => slot.ItemId == Apple).Sum(slot => slot.Count).Should().Be(3,
            "an old twin is not proof that this mail was written");
    }

    private sealed class Faults : IDbTransactionInterceptor, IDbCommandInterceptor
    {
        private bool failNextRead;

        public bool LoseNextCommitAnswer { get; set; }
        public bool FailReadAfterLostAnswer { get; set; }
        public bool FailNextCount { get; set; }

        public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!LoseNextCommitAnswer)
                return Task.CompletedTask;

            LoseNextCommitAnswer = false;
            failNextRead = FailReadAfterLostAnswer;
            throw new DbUpdateException("the answer to the commit was lost");
        }

        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (failNextRead && command.CommandText.Contains("Characters"))
            {
                failNextRead = false;
                throw new DbUpdateException("the database is still coming back");
            }

            if (FailNextCount && command.CommandText.Contains("COUNT") && command.CommandText.Contains("Mails"))
            {
                FailNextCount = false;
                throw new DbUpdateException("the database went away mid-count");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class Retrying(ExecutionStrategyDependencies dependencies) : ExecutionStrategy(dependencies, 3, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => true;
    }

    private ServiceProvider Provider(Faults faults, bool retrying) => CreateProvider(
        db =>
        {
            db.Accounts.AddRange(
                new Account { Id = AliceId, Login = "alice", Password = "-" },
                new Account { Id = BobId, Login = "bob", Password = "-" });
            db.Characters.AddRange(
                new Character { Id = AliceId, AccountId = AliceId, Name = "Alice", Money = Purse },
                new Character { Id = BobId, AccountId = BobId, Name = "Bob" });
            db.Warehouses.Add(new Warehouse { AccountId = AliceId });
            db.SaveChanges();
        },
        data => data.GetItem(Apple).Returns(new ItemData { Num = Apple, Name = "Apples of Moradon", Countable = 1, Duration = 1, Weight = 1 }),
        useDatabase: options => options
            .UseSqlite(database, sqlite =>
            {
                if (retrying)
                    sqlite.ExecutionStrategy(dependencies => new Retrying(dependencies));
            })
            .AddInterceptors(faults));

    private static UserSession Online(ServiceProvider provider, int characterId, string name)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId, characterId);
        (session.Name, session.Level, session.Hp, session.Money) = (name, 50, 100, Purse);
        return session;
    }

    private static async Task<int> MailsAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Mails.CountAsync();
    }
}
