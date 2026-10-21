using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface ICharacterStatePersister
{
    Task<bool> SaveAsync(UserSession session, CancellationToken cancellationToken = default);
    Task<bool> SaveAndSealAsync(UserSession session, CancellationToken cancellationToken = default);
    Task<StateCommit> CommitAsync(UserSession session, Func<AppDbContext, Task<bool>> change, Action undo,
        CancellationToken cancellationToken = default, Func<AppDbContext, Task<bool>>? landed = null);
    Task<StateCommit> CommitTogetherAsync(UserSession session, Func<AppDbContext, Task<bool>> change, Action undo,
        CancellationToken cancellationToken = default, Func<AppDbContext, Task<bool>>? landed = null);
    Task SetOnlineStateAsync(int characterId, bool isOnline, CancellationToken cancellationToken = default);

    Task SaveQuestStateAsync(UserSession session, CancellationToken cancellationToken = default);
}

public enum StateCommit
{
    Unchanged,
    Saved,
    Failed,
}

public class CharacterStatePersister(
    IServiceScopeFactory scopeFactory,
    IPetService petService,
    ILogger<CharacterStatePersister> logger) : ICharacterStatePersister
{
    private readonly IServiceScopeFactory scopeFactory = scopeFactory;

    // Quest-state saves are fired on every quest milestone, which a misbehaving or
    // spamming client can trigger dozens of times per second. To stop that flooding
    // the DB connection pool, saves are coalesced per character (repeated requests
    // collapse into a single in-flight save plus one pending re-save) and the total
    // number of concurrent quest writes is capped server-wide.
    private const int MaxConcurrentQuestSaves = 8;
    private readonly SemaphoreSlim questSaveGate = new(MaxConcurrentQuestSaves);
    private readonly ConcurrentDictionary<int, QuestSaveSlot> questSaveSlots = new();

    private sealed class QuestSaveSlot
    {
        public bool Running;
        public bool Dirty;
        public UserSession Session = null!;
    }

    public Task<bool> SaveAsync(UserSession session, CancellationToken cancellationToken = default) =>
        SaveAsync(session, seal: false, cancellationToken);

    public Task<bool> SaveAndSealAsync(UserSession session, CancellationToken cancellationToken = default) =>
        SaveAsync(session, seal: true, cancellationToken);

    private async Task<bool> SaveAsync(UserSession session, bool seal, CancellationToken cancellationToken)
    {
        if (session.IsBot)
            return false;

        await session.CharacterPersistenceGate.WaitAsync(cancellationToken);
        try
        {
            if (session.SavesSealed)
                return false;

            try
            {
                return await SaveUnderGateAsync(session);
            }
            finally
            {
                if (seal)
                    session.SealSaves();
            }
        }
        finally
        {
            session.CharacterPersistenceGate.Release();
        }
    }

    private async Task<bool> SaveUnderGateAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var warehouseRepository = scope.ServiceProvider.GetRequiredService<IWarehouseRepository>();
        var dailyOpRepository = scope.ServiceProvider.GetRequiredService<IUserDailyOpRepository>();
        var accountRepository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
        var userSessionCharacterMapper = scope.ServiceProvider.GetRequiredService<IUserSessionCharacterMapper>();
        var character = await characterRepository.GetById(session.CharacterId);
        if (character == null)
            return false;

        var warehouse = await warehouseRepository.GetOrCreateByAccountId(session.AccountId);
        var dailyOp = await dailyOpRepository.GetOrCreateByCharacterId(session.CharacterId);
        var account = await accountRepository.GetById(session.AccountId);

        // One snapshot under the lock, so a sale landing mid-save is saved whole or not at all.
        session.WithLock(s =>
        {
            ApplyToCharacter(s, character, userSessionCharacterMapper);
            userSessionCharacterMapper.ApplyToWarehouse(s, warehouse);
            userSessionCharacterMapper.ApplyToDailyOps(s, dailyOp);
            if (account != null)
                userSessionCharacterMapper.ApplyToAccount(s, account);
        });
        await characterRepository.UpdateAsync(character);
        await warehouseRepository.UpdateAsync(warehouse);
        await dailyOpRepository.UpdateAsync(dailyOp);
        if (account != null)
            await accountRepository.UpdateAsync(account);
        await petService.SaveAsync(session);

        return true;
    }

    // A change that moves neither the purse nor the bag tells whether it was written through landed.
    public Task<StateCommit> CommitAsync(UserSession session, Func<AppDbContext, Task<bool>> change, Action undo,
        CancellationToken cancellationToken = default, Func<AppDbContext, Task<bool>>? landed = null) =>
        CommitAsync(session, change, undo, landed, together: false, cancellationToken);

    // The change may save rows of its own, as it and the character commit in one transaction.
    public Task<StateCommit> CommitTogetherAsync(UserSession session, Func<AppDbContext, Task<bool>> change, Action undo,
        CancellationToken cancellationToken = default, Func<AppDbContext, Task<bool>>? landed = null) =>
        CommitAsync(session, change, undo, landed, together: true, cancellationToken);

    private async Task<StateCommit> CommitAsync(UserSession session, Func<AppDbContext, Task<bool>> change, Action undo,
        Func<AppDbContext, Task<bool>>? landed, bool together, CancellationToken cancellationToken)
    {
        await session.CharacterPersistenceGate.WaitAsync(cancellationToken);
        try
        {
            if (session.SavesSealed)
                return StateCommit.Unchanged;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mapper = scope.ServiceProvider.GetRequiredService<IUserSessionCharacterMapper>();
            // A store that retries runs a transaction as one attempt, which throws only before the change runs, so the change never runs twice.
            var attempt = together
                ? await db.Database.CreateExecutionStrategy().ExecuteAsync(
                    () => AttemptAsync(session, db, mapper, change, landed, together, cancellationToken))
                : await AttemptAsync(session, db, mapper, change, landed, together, cancellationToken);
            if (attempt is not { Failure: { } exception, Written: { } written })
                return attempt.Outcome;

            if (await WasWrittenAsync(written, attempt.Stored, landed))
            {
                logger.LogWarning(exception, "The commit of {Name} reported a failure but was written; the change stands", session.Name);
                return StateCommit.Saved;
            }

            logger.LogError(exception, "Could not commit {Name}; the change is undone", session.Name);
            undo();
            return StateCommit.Failed;
        }
        finally
        {
            session.CharacterPersistenceGate.Release();
        }
    }

    private sealed record Attempt(StateCommit Outcome, Exception? Failure = null, Character? Written = null, (int Money, byte[] Items) Stored = default);

    private async Task<Attempt> AttemptAsync(UserSession session, AppDbContext db, IUserSessionCharacterMapper mapper,
        Func<AppDbContext, Task<bool>> change, Func<AppDbContext, Task<bool>>? landed, bool together, CancellationToken cancellationToken)
    {
        var transaction = together ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        try
        {
            var character = await db.Characters.FindAsync([session.CharacterId], cancellationToken);
            if (character == null)
            {
                logger.LogError("Could not commit {Name}: character {CharacterId} is not stored", session.Name, session.CharacterId);
                return new(StateCommit.Failed);
            }

            // The warehouse holds escrowed offers that found no room in the bag.
            var warehouse = await db.Warehouses.SingleOrDefaultAsync(entry => entry.AccountId == session.AccountId, cancellationToken)
                ?? db.Warehouses.Add(new Warehouse { AccountId = session.AccountId }).Entity;
            var stored = (character.Money, character.Items);
            try
            {
                if (!await change(db))
                    return new(StateCommit.Unchanged);

                session.WithLock(active =>
                {
                    ApplyToCharacter(active, character, mapper);
                    mapper.ApplyToWarehouse(active, warehouse);
                });
                if (transaction == null)
                {
                    await SaveOnceAsync(db, character, stored, landed, cancellationToken);
                }
                else
                {
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }

                return new(StateCommit.Saved);
            }
            catch (Exception exception)
            {
                return new(StateCommit.Failed, exception, character, stored);
            }
        }
        finally
        {
            // The check for a lost answer must not see this attempt's own uncommitted rows.
            await EndAsync(transaction);
        }
    }

    private async Task EndAsync(IDbContextTransaction? transaction)
    {
        if (transaction == null)
            return;

        try
        {
            await transaction.DisposeAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not end a commit's transaction");
        }
    }

    // A store that retries a save runs it in a transaction and first asks whether a lost answer was a write.
    private async Task SaveOnceAsync(AppDbContext db, Character character, (int Money, byte[] Items) stored,
        Func<AppDbContext, Task<bool>>? landed, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        if (!strategy.RetriesOnFailure)
        {
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // A check that cannot read throws, so the store retries the check, not the save.
        await strategy.ExecuteInTransactionAsync(
            token => db.SaveChangesAsync(acceptAllChangesOnSuccess: false, token),
            _ => WasWrittenAsync(character, stored, landed, rethrow: true),
            cancellationToken);
        db.ChangeTracker.AcceptAllChanges();
    }

    // A save can be written and still report a failure; undoing it then would duplicate what it moved.
    private async Task<bool> WasWrittenAsync(Character written, (int Money, byte[] Items) before, Func<AppDbContext, Task<bool>>? landed,
        bool rethrow = false)
    {
        var moved = written.Money != before.Money || !written.Items.AsSpan().SequenceEqual(before.Items);
        if (!moved && landed == null)
            return false;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!moved)
                return await landed!(db);

            var now = await db.Characters.AsNoTracking()
                .Where(c => c.Id == written.Id)
                .Select(c => new { c.Money, c.Items })
                .FirstOrDefaultAsync();
            return now != null && now.Money == written.Money && now.Items.AsSpan().SequenceEqual(written.Items);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !rethrow)
        {
            logger.LogError(exception, "Could not check whether character {CharacterId}'s commit was written", written.Id);
            return false;
        }
    }

    private static void ApplyToCharacter(UserSession session, Character character, IUserSessionCharacterMapper mapper)
    {
        var destination = (character.MapId, character.X, character.Y, character.Z);
        mapper.ApplyToCharacter(session, character);
        if (session.NationTransferCommitted)
            (character.MapId, character.X, character.Y, character.Z) = destination;
    }

    public async Task SetOnlineStateAsync(int characterId, bool isOnline, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var character = await characterRepository.GetById(characterId);
        if (character == null)
            return;

        character.IsOnline = isOnline;
        character.LastOnlineTime = DateTime.UtcNow;

        await characterRepository.UpdateAsync(character);
    }

    public Task SaveQuestStateAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        if (session.IsBot)
            return Task.CompletedTask;

        var slot = questSaveSlots.GetOrAdd(session.CharacterId, static _ => new QuestSaveSlot());
        lock (slot)
        {
            slot.Session = session;
            slot.Dirty = true;
            if (slot.Running)
                return Task.CompletedTask;
            slot.Running = true;
        }

        _ = DrainQuestSaveSlotAsync(session.CharacterId, slot);
        return Task.CompletedTask;
    }

    private async Task DrainQuestSaveSlotAsync(int characterId, QuestSaveSlot slot)
    {
        while (true)
        {
            UserSession session;
            lock (slot)
            {
                if (!slot.Dirty)
                {
                    slot.Running = false;
                    return;
                }

                slot.Dirty = false;
                session = slot.Session;
            }

            await questSaveGate.WaitAsync();
            try
            {
                await PersistQuestBlobAsync(session);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Quest state persist failed for {CharacterId}", characterId);
            }
            finally
            {
                questSaveGate.Release();
            }
        }
    }

    private async Task PersistQuestBlobAsync(UserSession session)
    {
        await session.CharacterPersistenceGate.WaitAsync();
        try
        {
            if (session.SavesSealed)
                return;

            using var scope = scopeFactory.CreateScope();
            var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
            // Direct single-column UPDATE — no full character row read or rewrite.
            await characterRepository.UpdateQuestDataAsync(session.CharacterId, session.SerializeQuestData());
        }
        finally
        {
            session.CharacterPersistenceGate.Release();
        }
    }
}
