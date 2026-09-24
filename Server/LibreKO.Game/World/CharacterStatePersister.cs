using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface ICharacterStatePersister
{
    Task<bool> SaveAsync(UserSession session, CancellationToken cancellationToken = default);
    Task SetOnlineStateAsync(int characterId, bool isOnline, CancellationToken cancellationToken = default);

    Task SaveQuestStateAsync(UserSession session, CancellationToken cancellationToken = default);
}

public class CharacterStatePersister(
    IServiceScopeFactory scopeFactory,
    ILogger<CharacterStatePersister> logger) : ICharacterStatePersister
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    // Quest-state saves are fired on every quest milestone, which a misbehaving or
    // spamming client can trigger dozens of times per second. To stop that flooding
    // the DB connection pool, saves are coalesced per character (repeated requests
    // collapse into a single in-flight save plus one pending re-save) and the total
    // number of concurrent quest writes is capped server-wide.
    private const int MaxConcurrentQuestSaves = 8;
    private readonly SemaphoreSlim _questSaveGate = new(MaxConcurrentQuestSaves);
    private readonly ConcurrentDictionary<int, QuestSaveSlot> _questSaveSlots = new();

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

        using var scope = _scopeFactory.CreateScope();
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

        userSessionCharacterMapper.ApplyToCharacter(session, character);
        userSessionCharacterMapper.ApplyToWarehouse(session, warehouse);
        userSessionCharacterMapper.ApplyToDailyOps(session, dailyOp);
        if (account != null)
            userSessionCharacterMapper.ApplyToAccount(session, account);
        await characterRepository.UpdateAsync(character);
        await warehouseRepository.UpdateAsync(warehouse);
        await dailyOpRepository.UpdateAsync(dailyOp);
        if (account != null)
            await accountRepository.UpdateAsync(account);

        return true;
    }

    public async Task SetOnlineStateAsync(int characterId, bool isOnline, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
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

        var slot = _questSaveSlots.GetOrAdd(session.CharacterId, static _ => new QuestSaveSlot());
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

            await _questSaveGate.WaitAsync();
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
                _questSaveGate.Release();
            }
        }
    }

    private async Task PersistQuestBlobAsync(UserSession session)
    {
        using var scope = _scopeFactory.CreateScope();
        var characterRepository = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        // Direct single-column UPDATE — no full character row read or rewrite.
        await characterRepository.UpdateQuestDataAsync(session.CharacterId, session.SerializeQuestData());
    }
}
