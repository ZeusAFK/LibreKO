using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class CharacterCommitTests : GameTestBase
{
    private const int CharacterId = 1401;
    private const int Purse = 1_000;
    private const int Price = 100;
    private const int Ring = InventoryConstants.CypherRing;

    [Fact]
    public async Task AFailedCommitThatLeftGoldAndBagAloneIsUndone()
    {
        var faults = new FailsBeforeWriting();
        using var provider = CreateProvider(
            db => db.Characters.Add(new Character { Id = CharacterId, AccountId = CharacterId, Name = "Committer", Money = Purse }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var session = Session(provider);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Characters.SingleAsync(c => c.Id == CharacterId);
            row.Items = session.SavedItems;
            await db.SaveChangesAsync();
        }
        var undone = false;
        faults.Armed = true;

        var outcome = await provider.GetRequiredService<ICharacterStatePersister>().CommitAsync(session, db =>
        {
            db.Bounties.Add(new Bounty { PosterCharacterId = CharacterId, TargetCharacterId = CharacterId + 1, PostedAt = DateTime.UtcNow });
            return Task.FromResult(true);
        }, () => undone = true);

        outcome.Should().Be(StateCommit.Failed, "an unchanged character row cannot tell whether the save was written");
        undone.Should().BeTrue();
    }

    [Fact]
    public async Task AFailedCommitWhoseCharacterRowWasNotWrittenIsUndone()
    {
        var faults = new FailsBeforeWriting();
        using var provider = CreateProvider(
            db => db.Characters.Add(new Character { Id = CharacterId, AccountId = CharacterId, Name = "Committer", Money = Purse }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var session = Session(provider);
        var undone = false;
        faults.Armed = true;

        var outcome = await provider.GetRequiredService<ICharacterStatePersister>().CommitAsync(session, _ =>
        {
            session.Money -= Price;
            return Task.FromResult(true);
        }, () =>
        {
            undone = true;
            session.Money += Price;
        });

        outcome.Should().Be(StateCommit.Failed);
        undone.Should().BeTrue("the stored gold is still the old gold");
        session.Money.Should().Be(Purse);
    }

    [Fact]
    public async Task AFailedCommitThatMovedOnlyItemsIsUndone()
    {
        var faults = new FailsBeforeWriting();
        using var provider = CreateProvider(
            db => db.Characters.Add(new Character { Id = CharacterId, AccountId = CharacterId, Name = "Committer", Money = Purse }),
            configureServices: services => services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(faults)));
        var session = Session(provider);
        var slot = session.Inventory[InventoryConstants.InventoryStart];
        slot.ItemId = Ring;
        slot.Count = 1;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Characters.SingleAsync(c => c.Id == CharacterId);
            row.Items = session.SavedItems;
            await db.SaveChangesAsync();
        }
        var undone = false;
        faults.Armed = true;

        var outcome = await provider.GetRequiredService<ICharacterStatePersister>().CommitAsync(session, _ =>
        {
            slot.Clear();
            return Task.FromResult(true);
        }, () => undone = true);

        outcome.Should().Be(StateCommit.Failed, "the stored bag still holds the ring, though the gold matches");
        undone.Should().BeTrue();
    }

    [Fact]
    public async Task NothingIsWrittenAfterTheFinalSave()
    {
        using var provider = CreateProvider(
            db => db.Characters.Add(new Character { Id = CharacterId, AccountId = CharacterId, Name = "Committer", Money = Purse }));
        var session = Session(provider);
        var persister = provider.GetRequiredService<ICharacterStatePersister>();
        var changed = false;

        (await persister.SaveAndSealAsync(session)).Should().BeTrue();
        session.Money = Purse - Price;
        var saved = await persister.SaveAsync(session);
        var committed = await persister.CommitAsync(session, _ =>
        {
            changed = true;
            return Task.FromResult(true);
        }, () => { });

        saved.Should().BeFalse();
        committed.Should().Be(StateCommit.Unchanged);
        changed.Should().BeFalse("a closed session makes no more changes");
        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.SingleAsync(c => c.Id == CharacterId))
            .Money.Should().Be(Purse, "a newer session may already have loaded the account");
    }

    private static UserSession Session(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, CharacterId, CharacterId);
        session.Name = "Committer";
        session.Money = Purse;
        return session;
    }

    private sealed class FailsBeforeWriting : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Armed ? throw new DbUpdateException("the database went away") : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
