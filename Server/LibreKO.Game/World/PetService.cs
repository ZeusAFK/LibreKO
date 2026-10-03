using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IPetService
{
    Task LoadAsync(UserSession session);
    Task<IReadOnlyDictionary<int, PetItemInfo>> DescribeAsync(IEnumerable<ItemSlot> slots);
    PetItemInfo? ItemInfo(UserSession session, ItemSlot slot);
    Task HatchAsync(UserSession session, int npcId, int eggItemId, byte bagSlot, string name);
    Task<bool> SummonAsync(UserSession session);
    Task DismissAsync(UserSession session);
    Task SetModeAsync(UserSession session, PetMode mode);
    Task FeedAsync(UserSession session, byte bagSlot, int itemId);
    Task TickAsync(UserSession session, long nowTicks);
    Task AwardKillAsync(NpcInstance monster);
    Task SaveAsync(UserSession session);
}

public sealed class PetService(
    SessionManager sessionManager,
    IGameDataService gameData,
    IServiceScopeFactory scopeFactory,
    ILogger<PetService> logger) : IPetService
{
    public const int HatchedPetItem = 610_001_000;
    public const int FamiliarSummonSkill = 500_117;
    public const int FamiliarChannelingSkill = 500_118;
    public const short HatchedModelId = 25_500;
    public const short HatchedSize = 100;
    public const short SatisfactionDecayPerMinute = 100;
    public const short ResummonSatisfaction = 500;
    public static readonly TimeSpan SatisfactionDecayInterval = TimeSpan.FromMinutes(1);
    private const char FirstNameCharacter = '!';
    private const char LastNameCharacter = '~';

    private readonly ConcurrentDictionary<int, Pet> _pets = new();

    public Task LoadAsync(UserSession session) => CacheAsync(session.Inventory);

    public async Task<IReadOnlyDictionary<int, PetItemInfo>> DescribeAsync(IEnumerable<ItemSlot> slots)
    {
        var linked = slots.Where(slot => slot.IsLinked).ToArray();
        await CacheAsync(linked);
        var described = new Dictionary<int, PetItemInfo>();
        foreach (var slot in linked)
            if (_pets.TryGetValue(slot.UniqueId, out var pet))
                described[slot.UniqueId] = PetItemInfo.From(pet, LevelOf(pet.Level));
        return described;
    }

    private async Task CacheAsync(IEnumerable<ItemSlot> slots)
    {
        var ids = slots.Where(slot => slot.IsLinked).Select(slot => slot.UniqueId)
            .Where(id => !_pets.ContainsKey(id)).Distinct().ToArray();
        if (ids.Length == 0)
            return;

        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPetRepository>();
        foreach (var pet in await repository.GetByIds(ids))
            _pets[pet.Id] = pet;
    }

    public PetItemInfo? ItemInfo(UserSession session, ItemSlot slot)
    {
        if (!slot.IsLinked || !_pets.TryGetValue(slot.UniqueId, out var pet))
            return null;

        return PetItemInfo.From(pet, LevelOf(pet.Level));
    }

    public async Task HatchAsync(UserSession session, int npcId, int eggItemId, byte bagSlot, string name)
    {
        if (session.Trade.IsTrading || session.Trade.IsMerchanting || session.IsGathering || session.Hp <= 0)
        {
            await session.Client.SendPacket(PetPacketWriter.HatchRefused(HatchRefusal.Failed));
            return;
        }

        if (!IsValidName(name))
        {
            await session.Client.SendPacket(PetPacketWriter.HatchRefused(HatchRefusal.InvalidName));
            return;
        }

        var trainerNearby = sessionManager.Regions.GetNearbyNpcs(session)
            .Any(npc => npc.NpcId == npcId && npc.IsAlive && npc.ZoneId == session.ZoneId && IsInRange(session, npc));
        if (!trainerNearby || bagSlot >= InventoryConstants.HaveMax)
        {
            await session.Client.SendPacket(PetPacketWriter.HatchRefused(HatchRefusal.Failed));
            return;
        }

        var slot = session.Inventory[InventoryConstants.SlotMax + bagSlot];
        var egg = gameData.GetItem(eggItemId);
        var hatched = gameData.GetItem(HatchedPetItem);
        if (slot.ItemId != eggItemId || slot.IsLinked || egg?.Kind != (byte)ItemKind.PetEgg || hatched == null
            || slot.State is ItemFlag.Sealed or ItemFlag.Duplicate or ItemFlag.Rented)
        {
            await session.Client.SendPacket(PetPacketWriter.HatchRefused(HatchRefusal.CannotHatch));
            return;
        }

        var level = LevelOf(Pet.StartLevel);
        if (level == null)
        {
            await session.Client.SendPacket(PetPacketWriter.HatchRefused(HatchRefusal.Failed));
            return;
        }

        Pet pet;
        using (var scope = scopeFactory.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IPetRepository>();
            if (await repository.NameExists(name))
            {
                await session.Client.SendPacket(PetPacketWriter.HatchNameTaken());
                return;
            }

            pet = await repository.CreateAsync(new Pet
            {
                Name = name,
                Level = Pet.StartLevel,
                Hp = level.MaxHp,
                Mp = level.MaxMp,
                Satisfaction = Pet.HatchedSatisfaction,
                ModelId = HatchedModelId,
                Size = HatchedSize,
                Class = (byte)hatched.Damage,
                Items = PetInventory.Save(PetInventory.Load(null)),
            });
        }

        _pets[pet.Id] = pet;
        var placed = session.WithLock(s =>
        {
            var current = s.Inventory[InventoryConstants.SlotMax + bagSlot];
            if (current.ItemId != eggItemId)
                return false;

            current.Clear();
            current.ItemId = HatchedPetItem;
            current.Count = 1;
            current.Durability = hatched.Duration;
            current.UniqueId = pet.Id;
            return true;
        });

        if (!placed)
        {
            await session.Client.SendPacket(PetPacketWriter.HatchRefused(HatchRefusal.Failed));
            return;
        }

        logger.LogInformation("{Name} hatched familiar {PetName} (#{PetId})", session.Name, pet.Name, pet.Id);
        await session.Client.SendPacket(PetPacketWriter.Hatched(
            HatchedPetItem, bagSlot, pet.Id, pet.Name, pet.Class, pet.Level,
            PetItemInfo.ExpPercentOf(pet.Exp, level), pet.Satisfaction));
    }

    public async Task<bool> SummonAsync(UserSession session)
    {
        if (session.Pet is { IsSummoned: true } || session.Hp <= 0 || !ZoneRules.AllowsPets(session.ZoneId))
            return false;

        var slot = session.Inventory[InventoryConstants.Pet];
        if (!slot.IsLinked || gameData.GetItem(slot.ItemId)?.Kind != (byte)ItemKind.PetItem)
            return false;

        if (!_pets.TryGetValue(slot.UniqueId, out var pet))
        {
            await LoadAsync(session);
            if (!_pets.TryGetValue(slot.UniqueId, out pet))
                return false;
        }

        var level = LevelOf(pet.Level);
        if (level == null)
            return false;

        if (pet.Satisfaction <= 0)
            pet.Satisfaction = ResummonSatisfaction;
        pet.Hp = level.MaxHp;
        pet.Mp = level.MaxMp;

        var state = new PetState(pet, slot.ItemId);
        var npc = BuildNpc(session, state, level);
        state.Npc = npc;
        session.Pet = state;

        sessionManager.Regions.SpawnNpc(npc);
        await sessionManager.Regions.BroadcastFromNpc(npc, NpcPacketMapper.BuildInOutPacket(npc, InOutType.In));
        await session.Client.SendPacket(PetPacketWriter.Summoned(PetPacketWriter.InfoOf(state, level)));

        logger.LogDebug("{Name} summoned familiar {PetName} as NPC {NpcId}", session.Name, pet.Name, npc.UniqueId);
        return true;
    }

    public async Task DismissAsync(UserSession session)
    {
        if (session.Pet is not { } state)
            return;

        session.Pet = null;
        if (state.Npc is { } npc)
        {
            npc.Hp = 0;
            npc.State = NpcState.Dead;
            await sessionManager.Regions.BroadcastFromNpc(npc, NpcPacketMapper.BuildInOutPacket(npc, InOutType.Out));
            sessionManager.Regions.RemoveNpc(npc);
        }

        await session.Client.SendPacket(PetPacketWriter.Died(state.Record.Id));
        await PersistAsync(state);
    }

    public async Task SetModeAsync(UserSession session, PetMode mode)
    {
        if (session.Pet is not { IsSummoned: true } state)
        {
            await session.Client.SendPacket(PetPacketWriter.ModeChanged(mode));
            return;
        }

        state.Mode = mode;
        if (mode != PetMode.Attack)
            state.TargetNpcId = PetState.NoTarget;
        await session.Client.SendPacket(PetPacketWriter.ModeChanged(mode));
    }

    public async Task FeedAsync(UserSession session, byte bagSlot, int itemId)
    {
        if (session.Pet is not { IsSummoned: true } state || bagSlot >= InventoryConstants.HaveMax)
        {
            await session.Client.SendPacket(PetPacketWriter.FoodRefused(bagSlot, itemId));
            return;
        }

        var food = gameData.GetItem(itemId);
        var abs = InventoryConstants.SlotMax + bagSlot;
        short increase = 0;
        short countLeft = 0;
        var eaten = food?.Kind == (byte)ItemKind.PetFood && session.WithLock(s =>
        {
            var slot = s.Inventory[abs];
            if (slot.ItemId != itemId || slot.Count == 0 || slot.State is ItemFlag.Duplicate)
                return false;

            var before = state.Record.Satisfaction;
            state.Record.Satisfaction = (short)Math.Min(Pet.MaxSatisfaction, before + Math.Max(0, (int)food.Damage));
            increase = (short)(state.Record.Satisfaction - before);
            slot.Count--;
            if (slot.Count == 0)
                slot.Clear();
            countLeft = (short)slot.Count;
            return true;
        });

        if (!eaten)
        {
            await session.Client.SendPacket(PetPacketWriter.FoodRefused(bagSlot, itemId));
            return;
        }

        await session.Client.SendPacket(PetPacketWriter.Fed(bagSlot, itemId, countLeft, increase));
        await SendSatisfactionAsync(session, state);
    }

    public async Task TickAsync(UserSession session, long nowTicks)
    {
        if (session.Pet is not { IsSummoned: true } state)
            return;

        if (nowTicks - state.LastSatisfactionTicks < SatisfactionDecayInterval.Ticks)
            return;

        state.LastSatisfactionTicks = nowTicks;
        state.Record.Satisfaction = (short)Math.Max(0, state.Record.Satisfaction - SatisfactionDecayPerMinute);
        if (state.Record.Satisfaction <= 0)
        {
            logger.LogDebug("Familiar of {Name} ran out of satisfaction and left", session.Name);
            await DismissAsync(session);
            return;
        }

        await SendSatisfactionAsync(session, state);
    }

    public async Task AwardKillAsync(NpcInstance monster)
    {
        if (monster.PetDamage.IsEmpty || monster.Experience <= 0)
            return;

        var maxHp = Math.Max(1, monster.MaxHp);
        foreach (var (ownerId, damage) in monster.PetDamage)
        {
            var owner = sessionManager.GetByCharacterId(ownerId);
            if (owner?.Pet is not { IsSummoned: true } state)
                continue;

            var share = (long)Math.Ceiling((double)monster.Experience * Math.Min(damage, maxHp) / maxHp);
            await GainExpAsync(owner, state, share);
        }

        monster.PetDamage.Clear();
    }

    public async Task SaveAsync(UserSession session)
    {
        if (session.Pet is { } state)
            await PersistAsync(state);
    }

    private async Task GainExpAsync(UserSession owner, PetState state, long gained)
    {
        if (gained <= 0)
            return;

        var pet = state.Record;
        var level = LevelOf(pet.Level);
        if (level == null)
            return;

        pet.Exp += gained;
        var leveled = false;
        while (pet.Level < PetState.MaxLevel && level is { Exp: > 0 } && pet.Exp >= level.Exp)
        {
            pet.Exp -= level.Exp;
            pet.Level++;
            leveled = true;
            level = LevelOf(pet.Level) ?? level;
        }

        if (pet.Level >= PetState.MaxLevel && level is { Exp: > 0 })
            pet.Exp = Math.Min(pet.Exp, level.Exp);

        if (leveled && state.Npc is { } npc)
        {
            pet.Hp = level.MaxHp;
            pet.Mp = level.MaxMp;
            ApplyLevel(npc, pet, level);
            await sessionManager.Regions.BroadcastFromNpc(npc, PetPacketWriter.LevelUp(npc.UniqueId));
            await owner.Client.SendPacket(PetPacketWriter.Summoned(PetPacketWriter.InfoOf(state, level)));
        }

        await owner.Client.SendPacket(PetPacketWriter.ExpChanged(
            gained, PetItemInfo.ExpPercentOf(pet.Exp, level), pet.Level, pet.Satisfaction));
    }

    private Task SendSatisfactionAsync(UserSession session, PetState state) =>
        session.Client.SendPacket(PetPacketWriter.Satisfaction(state.Record.Satisfaction, state.Npc?.UniqueId ?? 0));

    private async Task PersistAsync(PetState state)
    {
        state.SaveItems();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IPetRepository>();
            await repository.UpdateAsync(state.Record);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Saving familiar #{PetId} failed", state.Record.Id);
        }
    }

    private NpcInstance BuildNpc(UserSession owner, PetState state, PetLevelData level)
    {
        var npc = new NpcInstance
        {
            NpcId = 0,
            Name = state.Record.Name,
            PetOwnerName = owner.Name,
            NpcType = NpcData.TypePet,
            IsMonster = false,
            ModelId = state.Record.ModelId,
            Size = state.Record.Size,
            Nation = (EntityNation)owner.Nation,
            OwnerCharId = owner.CharacterId,
            ZoneId = owner.ZoneId,
            Room = owner.Room,
            X = owner.X - PetAiService.FollowDistance,
            Y = owner.Y,
            Z = owner.Z - PetAiService.FollowDistance,
            SpawnX = owner.X - PetAiService.FollowDistance,
            SpawnY = owner.Y,
            SpawnZ = owner.Z - PetAiService.FollowDistance,
            RespawnType = NpcRespawnType.Never,
            AttackDelay = PetAiService.AttackDelayMs,
            Speed1 = PetAiService.WalkSpeed,
            Speed2 = PetAiService.RunSpeed,
            HitRate = PetAiService.HitRate,
            EvadeRate = PetAiService.HitRate,
            Direction = 0,
        };
        ApplyLevel(npc, state.Record, level);
        return npc;
    }

    private static void ApplyLevel(NpcInstance npc, Pet pet, PetLevelData level)
    {
        npc.Level = pet.Level;
        npc.MaxHp = level.MaxHp;
        npc.Hp = Math.Max(1, (int)pet.Hp);
        npc.MaxMp = level.MaxMp;
        npc.Mp = pet.Mp;
        npc.Attack1 = level.Attack;
        npc.Ac = level.Defence;
    }

    private PetLevelData? LevelOf(byte level) =>
        gameData.PetLevelTable.TryGetValue(level, out var data) ? data : null;

    private static bool IsInRange(UserSession session, NpcInstance npc)
    {
        var dx = session.X - npc.X;
        var dz = session.Z - npc.Z;
        return dx * dx + dz * dz <= GameConstants.MaxNpcInteractionRangeSq;
    }

    public static bool IsValidName(string name) =>
        name.Length is > 0 and <= Pet.NameMaxLength
        && name.All(c => c is >= FirstNameCharacter and <= LastNameCharacter);
}
