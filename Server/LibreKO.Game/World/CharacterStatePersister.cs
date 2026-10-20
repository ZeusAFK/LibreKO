using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface ICharacterStatePersister
{
    Task<bool> SaveAsync(UserSession session, CancellationToken cancellationToken = default);
    Task<StateCommit> CommitAsync(UserSession session, Func<AppDbContext, Task<bool>> change, Action undo,
        CancellationToken cancellationToken = default);
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

    public async Task<bool> SaveAsync(UserSession session, CancellationToken cancellationToken = default)
    {
        if (session.IsBot)
            return false;

        await session.CharacterPersistenceGate.WaitAsync(cancellationToken);
        try
        {
            return await SaveUnderGateAsync(session);
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

    public async Task<StateCommit> CommitAsync(UserSession session, Func<AppDbContext, Task<bool>> change, Action undo,
        CancellationToken cancellationToken = default)
    {
        await session.CharacterPersistenceGate.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mapper = scope.ServiceProvider.GetRequiredService<IUserSessionCharacterMapper>();
            var character = await db.Characters.FindAsync([session.CharacterId], cancellationToken);
            if (character == null)
            {
                logger.LogError("Could not commit {Name}: character {CharacterId} is not stored", session.Name, session.CharacterId);
                return StateCommit.Failed;
            }

            try
            {
                if (!await change(db))
                    return StateCommit.Unchanged;

                session.WithLock(active => ApplyToCharacter(active, character, mapper));
                await db.SaveChangesAsync(cancellationToken);
                return StateCommit.Saved;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not commit {Name}; the change is undone", session.Name);
                undo();
                return StateCommit.Failed;
            }
        }
        finally
        {
            session.CharacterPersistenceGate.Release();
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
        using var scope = scopeFactory.CreateScope();
        var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        // Direct single-column UPDATE — no full character row read or rewrite.
        await characterRepository.UpdateQuestDataAsync(session.CharacterId, session.SerializeQuestData());
    }
}
