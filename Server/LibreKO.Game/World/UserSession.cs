using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.World;

public class UserSession
{
    public const int SelectMessageEventCount = 12;

    private readonly Lock sync = new();

    public void WithLock(Action<UserSession> mutator)
    {
        using var scope = sync.EnterScope();
        mutator(this);
    }

    public T WithLock<T>(Func<UserSession, T> reader)
    {
        using var scope = sync.EnterScope();
        return reader(this);
    }

    public static void WithBoth(UserSession a, UserSession b, Action<UserSession, UserSession> mutator)
    {
        if (ReferenceEquals(a, b))
        {
            a.WithLock(s => mutator(s, s));
            return;
        }

        var (first, second) = a.CharacterId < b.CharacterId ? (a, b) : (b, a);
        using var s1 = first.sync.EnterScope();
        using var s2 = second.sync.EnterScope();
        mutator(a, b);
    }

    public IClient Client { get; }
    public int CharacterId { get; }
    public int AccountId { get; }
    public SemaphoreSlim CharacterPersistenceGate { get; } = new(1, 1);
    public bool NationTransferCommitted { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountNation Nation { get; set; }
    public byte Race { get; set; }
    public short Class { get; set; }
    public byte Level { get; set; }
    public byte Face { get; set; }
    public int Hair { get; set; }
    public short StatPoints { get; set; }
    public int Money { get; set; }
    public long Experience { get; set; }
    public int Loyalty { get; set; }
    public int MonthlyLoyalty { get; set; }
    public int PlayMinutes { get; set; }
    public int MonstersDefeated { get; set; }
    public int PlayersDefeated { get; set; }
    public int Deaths { get; set; }
    public DateTime SessionStartedAt { get; set; } = DateTime.UtcNow;

    public int AccumulatedPlayMinutes() =>
        PlayMinutes + (int)Math.Max(0, (DateTime.UtcNow - SessionStartedAt).TotalMinutes);
    public int DailyLoyalty { get; set; }
    public int DailyPremiumLoyalty { get; set; }
    public bool IsGM { get; set; }
    public bool GmModeEnabled { get; set; } = true;
    public virtual bool IsBot => false;

    // Knight Cash (account-scoped premium currency).
    public int KnightCash { get; set; }
    public GameLanguage Language { get; set; } = GameLanguage.English;

    public string LanguageCode => Language switch
    {
        GameLanguage.Spanish => "es",
        GameLanguage.Chinese => "zh",
        _ => LibreKO.Quests.Localization.QuestTranslations.SourceLanguage,
    };

    // Last NPC the player killed; quest scripts read it.
    public int LastKilledNpcId { get; set; }

    public DateTime MarketPriceAskedAt { get; set; }

    // Position (stored as game coords * 10)
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public byte ZoneId { get; set; }
    public ushort Room { get; set; }
    public (byte ZoneId, float X, float Z)? InstanceReturn { get; set; }
    public short Direction { get; set; }

    // ArenaZones.NoArena unless standing inside a free-for-all arena region of the current zone.
    public byte ArenaId { get; set; }
    public bool IsInArena => ArenaId != ArenaZones.NoArena;

    public long DeathExpLoss { get; set; }

    public ushort MoveOldWillX { get; set; }
    public ushort MoveOldWillZ { get; set; }
    public ushort MoveOldWillY { get; set; }
    public short MoveOldSpeed { get; set; }
    public byte MoveOldEcho { get; set; }

    // Set when this player moved; the movement-broadcast service coalesces the GS_MOVE
    // fan-out to a fixed cadence (instead of one broadcast per received move packet).
    public volatile bool MovePending;

    // Speed-hack check: last validated position. Reset on warp/zone change.
    public float SpeedLastX { get; set; }
    public float SpeedLastZ { get; set; }

    // Derived stats
    public short MaxHp { get; set; }
    public short Hp { get; set; }
    public short MaxMp { get; set; }
    public short Mp { get; set; }

    // Combat stats (recalculated by SetUserAbility)
    public DerivedStats Stats { get; set; } = new();

    // Base stats (needed for recalculation)
    public byte Strength { get; set; }
    public byte Stamina { get; set; }
    public byte Dexterity { get; set; }
    public byte Intelligence { get; set; }
    public byte Magic { get; set; }

    // Visual
    public bool IsHidingHelmet { get; set; }
    public bool IsHidingCospre { get; set; }
    public NationRanks NationRanks { get; set; } = NationRanks.None;
    public InvisibilityType Invisibility { get; set; }
    public byte SoccerTeam { get; set; }
    public bool IsInvisible => Invisibility != InvisibilityType.None;
    public bool IsInfiltrating => Invisibility == InvisibilityType.Infiltration;

    public long LastCombatTicks { get; set; }
    public void MarkCombat() => LastCombatTicks = Environment.TickCount64;
    public bool IsInCombat =>
        LastCombatTicks != 0 && Environment.TickCount64 - LastCombatTicks < GameConstants.CombatStateSeconds * 1000L;
    public short TransformId { get; set; } // 0=none, NPC model ID when transformed
    public NpcInstance? SummonedGuard { get; set; }
    public bool IsTransformed => TransformId > 0;
    public int TransformSkillId { get; set; }

    // Clan/Knights
    public short KnightsId { get; set; }
    public int ClanOnlineCount { get; set; }
    public SiegeSide SiegeSide { get; set; }
    public int TowerHostId { get; set; }
    public byte PartyBoardType { get; set; }
    public byte MatchTeam { get; set; }
    public DateTime LastFullMoonShout { get; set; }
    public int GuardTowerNpcId { get; set; }
    public int KnightsPoints { get; set; }
    public byte KnightsFame { get; set; }
    public int ClanInviteFrom { get; set; }
    public short ClanInviteClanId { get; set; }
    public string KnightsName { get; set; } = string.Empty;
    public byte Fame { get; set; } // authority/fame level (captain, etc.)

    // Premium
    public const byte AccountStatusNone = 0;
    public const byte AccountStatusPremium = 1;

    public byte AccountStatus { get; set; } // 0=none, 1=premium, 2=PC room
    public DateTime? PremiumExpiry { get; set; }
    public byte PremiumService { get; set; }

    public short PremiumTime => PremiumService > 0 ? Account.RemainingHoursUntil(PremiumExpiry) : (short)0;
    public byte PremiumType => PremiumTime > 0 ? PremiumService : (byte)0;

    public const byte DrakiStageMin = 1;
    public const byte DrakiStageMax = 5;
    public const byte DrakiSubStageMin = 1;
    public const byte DrakiSubStageMax = 8;

    public GenieTimeBalance GenieTime { get; } = new();

    public short GenieHours => (short)Math.Min(Math.Ceiling(GenieTime.RemainingSeconds / 3600), short.MaxValue);

    public ushort GenieMinutes => (ushort)Math.Min(Math.Ceiling(GenieTime.RemainingSeconds / 60), ushort.MaxValue);

    private bool genieActive;
    public bool GenieActive
    {
        get { using var scope = sync.EnterScope(); return genieActive; }
        set
        {
            using var scope = sync.EnterScope();
            if (value) GenieTime.Resume(); else GenieTime.Pause();
            genieActive = value;
        }
    }

    public byte[] GenieOptions { get; set; } = [];

    public List<RepurchaseEntry> Repurchases { get; } = [];

    public byte DrakiStage { get; set; }
    public byte DrakiSubStage { get; set; }
    public byte DrakiEntranceLimit { get; set; } = DrakiTowerRules.MaxDailyEntrances;
    public DateTime DrakiEntranceLimitResetDate { get; set; } = DateTime.UtcNow.Date;

    // Party BBS
    public bool SeekingParty { get; set; }
    public string PartyBbsMessage { get; set; } = string.Empty;
    public short WantedClass { get; set; }

    public bool IsMining { get; set; }
    public bool IsFishing { get; set; }
    public DateTime LastGatherAttempt { get; set; }
    public DateTime LastPieceExchangeTime { get; set; } = DateTime.MinValue;
    public bool IsGathering => IsMining || IsFishing;

    // Dead, trading, keeping or setting up a stall, or gathering: every handler that moves an
    // item refuses while this holds, so nothing moves an item a trade or a stall counts on.
    public bool IsBusy =>
        Hp <= 0 || Trade.IsTrading || Trade.IsMerchanting || Trade.IsMerchantPreparing || IsGathering;

    // Pet companion
    public PetState? Pet { get; set; }
    public int LastStruckNpcId { get; set; }
    public long LastStruckTicks { get; set; }

    public int[] DailyOps { get; } = new int[UserDailyOp.Count];

    public int AttendanceDays { get; set; }
    public long SealedExperience { get; set; }
    public bool ExpSealOn { get; set; }
    public int AttendanceClaimedDays { get; set; }
    public byte AttendanceClaimedBonus { get; set; }
    public DateTime? AttendanceCheckedOn { get; set; }

    // Rebirth (WIZ_REBIRTH 0xD3). Snapshot stats stored at rebirth time.
    public short RebirthLevel { get; set; }
    public byte RebStr { get; set; }
    public byte RebSta { get; set; }
    public byte RebDex { get; set; }
    public byte RebIntel { get; set; }
    public byte RebMagic { get; set; }

    // Party
    public int PartyIndex { get; set; } = -1;
    public bool IsInParty => PartyIndex >= 0;
    private bool isPartyLeader;

    public bool IsPartyLeader
    {
        get => isPartyLeader;
        set
        {
            if (isPartyLeader == value) return;
            isPartyLeader = value;
            PartyLeaderChanged?.Invoke(this);
        }
    }

    public event Action<UserSession>? PartyLeaderChanged;

    public short ClanBattleEnemy { get; set; }

    // 850ms cooldown shared by PARTY_TARGET_NUMBER (0x1F) and PARTY_ALERT (0x20).
    public DateTime LastPartySignalTime { get; set; } = DateTime.MinValue;

    // Private chat
    public int PrivateChatUser { get; set; } = -1;
    public bool BlockPrivateChat { get; set; }
    public bool IsMuted { get; set; }
    public long LastChatTicks { get; set; }

    // Trade state (exchange, merchant, challenge)
    public TradeState Trade { get; } = new();

    // PvP Rival system
    public const int NoRival = -1;
    public int RivalId { get; set; } = NoRival;  // CharacterId of rival (-1 if none)
    public DateTime RivalExpiryTime { get; set; }
    public byte AngerGauge { get; set; } // 0-5 (5 = max)
    public bool HasRival => RivalId != NoRival;
    public bool HasFullAngerGauge => AngerGauge >= 5;

    // Active buffs: magicId -> ActiveBuff
    private readonly ConcurrentDictionary<int, ActiveBuff> activeBuffs = new();
    public IReadOnlyDictionary<int, ActiveBuff> ActiveBuffs => activeBuffs;
    public ConcurrentDictionary<int, ActiveOverTimeEffect> ActiveOverTimeEffects { get; } = new();

    public ConcurrentDictionary<int, long> SkillCooldowns { get; } = new();
    public int CastingSkillId { get; set; }
    public long CastReadyTicks { get; set; }
    public long CastCommitTicks { get; set; }
    public long CastExpireTicks { get; set; }
    public bool CastLaunched { get; set; }
    public ConcurrentDictionary<int, long> AcceptedCasts { get; } = new();
    public ConcurrentDictionary<int, int> PendingArrowHits { get; } = new();
    public long SkillBurstTicks { get; set; }
    public long NextSwingTicks { get; set; }
    public long NextShotTicks { get; set; }
    public long LastPotionTicks { get; set; }
    public long MageStatusImmuneUntilTicks { get; set; }

    // Buff type-specific state flags (recalculated from ActiveBuffs)
    public bool IsBlinded { get; set; }
    public bool BlockCurses { get; set; }
    public bool ReflectCurses { get; set; }
    public bool InstantCast { get; set; }
    public bool CanUseSkills { get; set; } = true;
    public long BlinkUntilTicks { get; set; }
    public bool IsBlinking => BlinkUntilTicks != 0;
    public bool BlinkOnArrival { get; set; }
    public ResurrectionOffer? PendingResurrection { get; set; }
    public int ControlSignature { get; set; }
    public bool CanUsePotions { get; set; } = true;
    public bool CanTeleport { get; set; } = true;
    public bool StealthProhibited { get; set; }
    public bool WeaponsDisabled { get; set; }
    public bool IsUndead { get; set; }
    public bool IsKaul { get; set; }
    public bool BlockPhysical { get; set; }
    public bool BlockMagic { get; set; }
    public bool HealStrikesEnemies { get; private set; }
    public bool MirrorDamage { get; set; }
    public byte MirrorDamageAmount { get; set; }
    public byte SpeedAmount { get; set; } = 100;
    public byte ManaAbsorbPct { get; set; }
    public byte MagicDamageReduction { get; set; } = 100;
    public byte ExpGainAmount { get; set; } = 100;
    public byte LoyaltyGainAmount { get; set; } = 100;
    public byte NoahGainAmount { get; set; } = 100;
    public byte PlayerAttackAmount { get; set; } = 100;
    public byte AttackAmount { get; set; } = 100;
    public short AttackSpeedAmount { get; set; } = 100;
    public short MagicAttackAmount { get; set; }
    public byte ReflectArmorType { get; set; }

    // Quest & NPC interaction state
    public QuestState Quest { get; } = new();
    public bool IsWarping { get; set; }          // True during zone change, prevents re-entry
    public byte KillerNpcType { get; set; }      // NpcType of NPC that killed this player (0 = PvP/alive)
    public bool IsSitting { get; set; }           // True when player is sitting (affects HP/MP regen rate)
    public bool InCombatStance { get; set; }

    public bool RankAuraHidden { get; set; }
    public bool DevilForm { get; set; }

    public Protocol.Writers.PlayerAppearanceState SizeState { get; set; } = Protocol.Writers.PlayerAppearanceState.Normal;

    // Skill point allocation: [0]=free, [1-4]=unused, [5]=cat1, [6]=cat2, [7]=cat3, [8]=master
    public byte[] SkillPoints { get; } = new byte[9];

    // Skill bar data (saved/loaded as binary blob)
    public byte[] SkillData { get; set; } = [];

    // Inventory: 73 slots (14 equipped + 28 inventory + 5 cospre + 2 bag slots + 24 bag items)
    public ItemSlot[] Inventory { get; } = new ItemSlot[InventoryConstants.InventoryTotal];

    // Warehouse: 192 slots
    public const int WarehouseMax = 192;
    public ItemSlot[] Warehouse { get; } = new ItemSlot[WarehouseMax];
    public int WarehouseMoney { get; set; }

    // VIP warehouse: 48 slots (account-scoped premium vault).
    public const int VipWarehouseMax = 48;
    public ItemSlot[] VipWarehouse { get; } = new ItemSlot[VipWarehouseMax];
    public DateTime VipVaultExpiry { get; set; }

    public bool VipVaultExpiryAnnounced { get; set; }

    public string VipPassword { get; set; } = string.Empty;

    public byte VipPasswordRequest { get; set; }

    public byte VipPasswordFailures { get; set; }

    public short KurianSp { get; set; }

    public GroundEffect Ground { get; set; }

    public short KurianMaxSp { get; set; }

    public string SealCode { get; set; } = string.Empty;

    // Region tracking
    public int RegionX { get; set; } = -1;
    public int RegionZ { get; set; } = -1;
    public long RegisteredRegionKey { get; set; } = RegionManager.NoRegionKey;

    public short GetPosX => (short)(X * 10);
    public short GetPosZ => (short)(Z * 10);
    public short GetPosY => (short)(Y * 10);

    public int NewRegionX => (int)(X / RegionManager.RegionSize);
    public int NewRegionZ => (int)(Z / RegionManager.RegionSize);

    public UserSession(IClient client, int characterId, int accountId)
    {
        Client = client;
        CharacterId = characterId;
        AccountId = accountId;

        for (int i = 0; i < Inventory.Length; i++)
            Inventory[i] = new ItemSlot();
        for (int i = 0; i < Warehouse.Length; i++)
            Warehouse[i] = new ItemSlot();
        for (int i = 0; i < VipWarehouse.Length; i++)
            VipWarehouse[i] = new ItemSlot();
    }

    public void RecalculateStats(CoefficientData coefficient, IGameDataService gameData)
    {
        Stats = AbilityCalculator.Calculate(
            Level, Strength, Stamina, Dexterity, Intelligence,
            Class, coefficient, Inventory, gameData, SkillPoints, TitleBonuses(gameData),
            new RebirthBonus(RebStr, RebSta, RebDex, RebIntel, RebMagic),
            UserSessionMagicState.BuffStatBonus(this),
            UserSessionMagicState.WeaponDamageBonus(this));
        UserSessionMagicState.ApplyBuffBonuses(this);
        ApplyFamiliarScrolls(coefficient, gameData);
        HealStrikesEnemies = HealStrike.Armed(Inventory, gameData.GetItemOps);
        if (ZoneId == ChaosRules.Zone)
            Stats.MaxHp = ChaosRules.MaxHp;
        MaxHp = Stats.MaxHp;
        MaxMp = Stats.MaxMp;
    }

    private void ApplyFamiliarScrolls(CoefficientData coefficient, IGameDataService gameData)
    {
        if (Pet is not { IsSummoned: true } familiar)
            return;

        foreach (var scroll in PetBag.OwnerScrolls(familiar.Items, gameData.GetItem))
        {
            Stats.MaxHp = (short)Math.Min(AbilityCalculator.MaxPlayerHp, Stats.MaxHp + scroll.MaxHpB);
            Stats.TotalAc = (short)(Stats.TotalAc + coefficient.Ac * scroll.Ac);
            Stats.TotalHit = (ushort)Math.Min(ushort.MaxValue, Stats.TotalHit + scroll.Damage);
        }
    }

    public byte GetStat(StatType stat) => stat switch
    {
        StatType.Strength => Strength,
        StatType.Stamina => Stamina,
        StatType.Dexterity => Dexterity,
        StatType.Intelligence => Intelligence,
        StatType.Magic => Magic,
        _ => 0,
    };

    public void ApplyBaseStats()
    {
        var baseStats = ProgressionTable.BaseStatsForClass(Class);
        Strength = baseStats.Strength;
        Stamina = baseStats.Stamina;
        Dexterity = baseStats.Dexterity;
        Intelligence = baseStats.Intelligence;
        Magic = baseStats.Magic;
    }

    public void ResetMasteryPoints()
    {
        SkillPoints[ProgressionTable.MasteryPoolSlot] = ProgressionTable.MasteryPointsForLevel(Level);
        for (var index = ProgressionTable.MasteryPoolSlot + 1; index < ProgressionTable.MasterySlotCount; index++)
            SkillPoints[index] = 0;
    }

    public void RecalculateStatsWithBuffs(IGameDataService gameData)
    {
        var coefficient = gameData.GetCoefficient(Class);
        if (coefficient == null) return;
        RecalculateStats(coefficient, gameData);
    }

    public void LoadItems(byte[] data)
        => UserSessionBinaryState.LoadItems(Inventory, data);

    public byte[] SerializeItems() => UserSessionBinaryState.SerializeItems(Inventory);

    public void LoadWarehouse(byte[] data)
        => UserSessionBinaryState.LoadWarehouse(Warehouse, data);

    public byte[] SerializeWarehouse() => UserSessionBinaryState.SerializeWarehouse(Warehouse);

    public void LoadVipWarehouse(byte[] data)
        => UserSessionBinaryState.LoadWarehouse(VipWarehouse, data);

    public byte[] SerializeVipWarehouse() => UserSessionBinaryState.SerializeWarehouse(VipWarehouse);

    public AchievementState Achievements { get; } = new();

    public short DisplayTitleId { get; set; }

    private AchievementTitleData? titleBonuses;

    public void InvalidateTitleBonuses() => titleBonuses = null;

    public AchievementTitleData TitleBonuses(IGameDataService gameData)
    {
        if (titleBonuses != null) return titleBonuses;

        var totals = new AchievementTitleData();
        foreach (var (achievementId, entry) in Achievements.Entries)
        {
            if (entry.State != AchievementProgressState.Claimed) continue;
            if (!gameData.AchievementTable.TryGetValue(achievementId, out var definition)) continue;
            if (definition.TitleId == 0) continue;
            if (!gameData.AchievementTitleTable.TryGetValue(definition.TitleId, out var title)) continue;
            totals.Add(title);
        }

        return titleBonuses = totals;
    }

    public byte[] SerializeQuestData() => UserSessionBinaryState.SerializeQuestData(
        Quest.QuestMap, Quest.QuestKillCountsMap, Quest.DailyCompletionDays);

    public void LoadQuestData(byte[] data)
    {
        UserSessionBinaryState.LoadQuestData(Quest.QuestMap, Quest.QuestKillCountsMap, data, Quest.DailyCompletionDays);
        Quest.SyncActiveQuestKillCounts();
    }

    public event Action<UserSession>? BuffsChanged;

    public void SetBuff(int skillId, ActiveBuff buff)
    {
        activeBuffs[skillId] = buff;
        BuffsChanged?.Invoke(this);
    }

    public bool RemoveBuff(int skillId) => RemoveBuff(skillId, out _);

    public bool RemoveBuff(int skillId, [MaybeNullWhen(false)] out ActiveBuff removed)
    {
        if (!activeBuffs.TryRemove(skillId, out removed))
            return false;

        BuffsChanged?.Invoke(this);
        return true;
    }

    public int RemoveBuffsWhere(Func<int, ActiveBuff, bool> match)
    {
        var removed = 0;
        foreach (var (skillId, buff) in activeBuffs)
        {
            if (match(skillId, buff) && activeBuffs.TryRemove(skillId, out _))
                removed++;
        }

        if (removed > 0)
            BuffsChanged?.Invoke(this);
        return removed;
    }

    public void ClearBuffs()
    {
        if (activeBuffs.IsEmpty)
            return;

        activeBuffs.Clear();
        BuffsChanged?.Invoke(this);
    }

    public IReadOnlyList<int> LiveBuffIds() =>
        activeBuffs
            .Where(entry => !entry.Value.IsExpired)
            .Select(entry => entry.Key)
            .Order()
            .ToList();

    public byte[] SerializeSavedMagic() => UserSessionMagicState.SerializeSavedMagic(this);

    public int DropVolatileMagic(IGameDataService gameData)
        => UserSessionMagicState.DropVolatileMagic(this, gameData);

    public void RebuildSpecialStates(IGameDataService gameData)
        => UserSessionMagicState.RebuildSpecialStates(this, gameData);

    public void LoadSavedMagic(byte[] data, IGameDataService gameData)
        => UserSessionMagicState.LoadSavedMagic(this, data, gameData);

    public ItemSlot GetEquippedItem(int slot)
    {
        if (slot < 0 || slot >= Inventory.Length)
            return new ItemSlot();
        return Inventory[slot];
    }

    public int FindSlotForItem(int itemId, IGameDataService gameData, ushort count = 1)
    {
        var itemData = gameData.GetItem(itemId);
        if (itemData == null) return -1;

        // Check for stackable items first
        if (itemData.Countable != 0)
        {
            for (int i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
            {
                if (Inventory[i].ItemId == itemId && Inventory[i].Count + count <= 9999)
                    return i;
            }
        }

        // Find empty slot
        for (int i = InventoryConstants.SlotMax; i < InventoryConstants.SlotMax + InventoryConstants.HaveMax; i++)
        {
            if (Inventory[i].IsEmpty)
                return i;
        }

        return -1;
    }

    // Hands every escrowed offer back: gold into the purse (and past the coin cap into the
    // warehouse), each item by identity into the bag. Call under this session's lock.
    public ExchangeReturn InitExchange(bool start)
    {
        var returned = new ExchangeReturn();
        foreach (var item in Trade.ExchangeItemList)
        {
            if (item.ItemId == InventoryConstants.ItemGold)
            {
                var gold = ReturnHeldGold(item.Count);
                returned.GoldToPurse += gold.ToPurse;
                returned.GoldToWarehouse += gold.ToWarehouse;
                returned.GoldLost += gold.Lost;
            }
            else if (!ReturnEscrowedItem(item, returned))
            {
                returned.Unplaced.Add(item);
            }
        }
        Trade.ExchangeItemList.Clear();

        if (!start)
        {
            Trade.ExchangeUser = -1;
            Trade.ExchangeOk = false;
            Trade.AskedForExchange = false;
        }

        return returned;
    }

    // Its own bag slot when that is still free (or holds the same stack), else any free bag slot,
    // else the warehouse, so a slot filled during the trade never costs the item. The bag slots
    // involved are noted in returned, for the client.
    public bool ReturnEscrowedItem(ExchangeItem item, ExchangeReturn? returned = null)
    {
        if (item.SrcPos is >= InventoryConstants.InventoryStart and < InventoryConstants.InventoryStart + InventoryConstants.HaveMax)
            returned?.BagSlots.Add(item.SrcPos);

        var slot = ItemPlacement.FindForReturn(
            Inventory, InventoryConstants.InventoryStart, InventoryConstants.HaveMax, item, item.SrcPos);
        if (slot >= 0)
        {
            ItemPlacement.Put(Inventory[slot], item);
            returned?.BagSlots.Add(slot);
            return true;
        }

        slot = ItemPlacement.FindForReturn(Warehouse, 0, WarehouseMax, item);
        if (slot < 0)
            return false;

        ItemPlacement.Put(Warehouse[slot], item);
        return true;
    }

    // Hands back the gold a buying stall still holds, the same way an offer's gold comes back.
    public (int ToPurse, int ToWarehouse, long Lost) ReleaseBuyMerchantGold() => WithLock(s =>
    {
        var held = s.Trade.BuyMerchantGold;
        s.Trade.BuyMerchantGold = 0;
        return s.ReturnHeldGold(held);
    });

    // The purse and warehouse gold as they are saved: gold a buying stall holds is still the
    // owner's, split the way closing the stall would split it. Read under the lock so a stall
    // opening or closing meanwhile is counted once.
    public int SavedMoney => WithLock(s => s.Money + s.SplitHeldGold(s.Trade.BuyMerchantGold).ToPurse);

    public int SavedWarehouseMoney => WithLock(s => s.WarehouseMoney + s.SplitHeldGold(s.Trade.BuyMerchantGold).ToWarehouse);

    // Gold held aside (an offer in a trade, a buying stall's purse) goes back into the purse up to
    // the coin cap and the rest into the warehouse, so a purse that grew meanwhile loses none of
    // it. Only what fits in neither is lost.
    private (int ToPurse, int ToWarehouse, long Lost) ReturnHeldGold(long amount)
    {
        var (toPurse, toWarehouse) = SplitHeldGold(amount);
        Money += toPurse;
        WarehouseMoney += toWarehouse;
        return (toPurse, toWarehouse, Math.Max(0, amount) - toPurse - toWarehouse);
    }

    private (int ToPurse, int ToWarehouse) SplitHeldGold(long amount)
    {
        const long coinMax = Protocol.ExchangePacketConstants.CoinMax;
        var toPurse = Math.Clamp(amount, 0, Math.Max(0, coinMax - Money));
        var toWarehouse = Math.Clamp(amount - toPurse, 0, Math.Max(0, coinMax - WarehouseMoney));
        return ((int)toPurse, (int)toWarehouse);
    }

    public void CompleteExchange()
    {
        Trade.ExchangeItemList.Clear();
        Trade.ExchangeUser = -1;
        Trade.ExchangeOk = false;
        Trade.AskedForExchange = false;
    }
}
