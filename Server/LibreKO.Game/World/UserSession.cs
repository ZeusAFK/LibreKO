using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.World;

public class UserSession
{
    public const int SelectMessageEventCount = 12;

    private readonly Lock _sync = new();

    public void WithLock(Action<UserSession> mutator)
    {
        using var scope = _sync.EnterScope();
        mutator(this);
    }

    public T WithLock<T>(Func<UserSession, T> reader)
    {
        using var scope = _sync.EnterScope();
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
        using var s1 = first._sync.EnterScope();
        using var s2 = second._sync.EnterScope();
        mutator(a, b);
    }

    public IClient Client { get; }
    public int CharacterId { get; }
    public int AccountId { get; }
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
    public bool IsGM { get; set; }
    public bool GmFxEnabled { get; set; } = true;

    // Knight Cash (account-scoped premium currency).
    public int KnightCash { get; set; }
    public GameLanguage Language { get; set; } = GameLanguage.English;

    public string LanguageCode => Language switch
    {
        GameLanguage.Spanish => "es",
        _ => LibreKO.Quests.Localization.QuestTranslations.SourceLanguage,
    };

    // Last NPC the player killed; quest scripts read it.
    public int LastKilledNpcId { get; set; }

    // Item ID the client has subscribed to upgrade-notice broadcasts for (WIZ_UPGRADE_NOTICE 0xB8).
    public int WatchedUpgradeItem { get; set; }

    // Position (stored as game coords * 10)
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public byte ZoneId { get; set; }
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
    public InvisibilityType Invisibility { get; set; }
    public bool IsInvisible => Invisibility != InvisibilityType.None;
    public short TransformId { get; set; } // 0=none, NPC model ID when transformed
    public bool IsTransformed => TransformId > 0;

    // Clan/Knights
    public short KnightsId { get; set; }
    public int KnightsPoints { get; set; }
    public byte KnightsFame { get; set; } // 1=chief, 2=vicechief, 5=trainee
    public string KnightsName { get; set; } = string.Empty;
    public byte Fame { get; set; } // authority/fame level (captain, etc.)

    // Premium
    public const byte AccountStatusNone = 0;
    public const byte AccountStatusPremium = 1;

    public byte AccountStatus { get; set; } // 0=none, 1=premium, 2=PC room
    public DateTime? PremiumExpiry { get; set; }
    public byte PremiumService { get; set; }

    public short PremiumTime => Account.RemainingHoursUntil(PremiumExpiry);
    public byte PremiumType => PremiumTime > 0 ? PremiumService : (byte)0;

    public const byte DrakiStageMin = 1;
    public const byte DrakiStageMax = 5;
    public const byte DrakiSubStageMin = 1;
    public const byte DrakiSubStageMax = 8;

    public DateTime? GenieExpiry { get; set; }

    public short GenieHours => Character.RemainingGenieHours(GenieExpiry);

    public ushort GenieMinutes => Character.RemainingGenieMinutes(GenieExpiry);

    public bool GenieActive { get; set; }

    public byte[] GenieOptions { get; set; } = [];

    public byte DrakiStage { get; set; }
    public byte DrakiSubStage { get; set; }

    // Party BBS
    public bool SeekingParty { get; set; }
    public string PartyBbsMessage { get; set; } = string.Empty;
    public short WantedClass { get; set; }

    public bool IsMining { get; set; }
    public bool IsFishing { get; set; }
    public DateTime LastGatherAttempt { get; set; }
    public DateTime LastPieceExchangeTime { get; set; } = DateTime.MinValue;
    public bool IsGathering => IsMining || IsFishing;

    // Pet companion
    public PetState? Pet { get; set; }
    public DateTime LastPetSatisfactionDecay { get; set; }

    public int[] DailyOps { get; } = new int[UserDailyOp.Count];

    public int AttendanceDays { get; set; }
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
    public bool IsPartyLeader { get; set; }

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
    public ConcurrentDictionary<int, ActiveBuff> ActiveBuffs { get; } = new();
    public ConcurrentDictionary<int, ActiveOverTimeEffect> ActiveOverTimeEffects { get; } = new();
    public PendingMagicExecution? PendingOverTimeExecution { get; set; }
    public long PendingOverTimeToken { get; set; }

    public ConcurrentDictionary<int, long> SkillCooldowns { get; } = new();
    public int CastingSkillId { get; set; }
    public long CastReadyTicks { get; set; }
    public long CastCommitTicks { get; set; }
    public long CastExpireTicks { get; set; }
    public ConcurrentDictionary<int, long> AcceptedCasts { get; } = new();
    public ConcurrentDictionary<int, int> PendingArrowHits { get; } = new();
    public long SkillBurstTicks { get; set; }
    public long LastPotionTicks { get; set; }

    // Buff type-specific state flags (recalculated from ActiveBuffs)
    public bool IsBlinded { get; set; }
    public bool BlockCurses { get; set; }
    public bool ReflectCurses { get; set; }
    public bool InstantCast { get; set; }
    public bool CanUseSkills { get; set; } = true;
    public bool CanUsePotions { get; set; } = true;
    public bool CanTeleport { get; set; } = true;
    public bool StealthProhibited { get; set; }
    public bool WeaponsDisabled { get; set; }
    public bool IsUndead { get; set; }
    public bool IsKaul { get; set; }
    public bool BlockPhysical { get; set; }
    public bool BlockMagic { get; set; }
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

    public string VipPassword { get; set; } = string.Empty;

    public byte VipPasswordRequest { get; set; }

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
            new RebirthBonus(RebStr, RebSta, RebDex, RebIntel, RebMagic));
        UserSessionMagicState.ApplyBuffBonuses(this, gameData, coefficient);
        MaxHp = Stats.MaxHp;
        MaxMp = Stats.MaxMp;
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

    private AchievementTitleData? _titleBonuses;

    public void InvalidateTitleBonuses() => _titleBonuses = null;

    public AchievementTitleData TitleBonuses(IGameDataService gameData)
    {
        if (_titleBonuses != null) return _titleBonuses;

        var totals = new AchievementTitleData();
        foreach (var (achievementId, entry) in Achievements.Entries)
        {
            if (entry.State != AchievementProgressState.Claimed) continue;
            if (!gameData.AchievementTable.TryGetValue(achievementId, out var definition)) continue;
            if (definition.TitleId == 0) continue;
            if (!gameData.AchievementTitleTable.TryGetValue(definition.TitleId, out var title)) continue;
            totals.Add(title);
        }

        return _titleBonuses = totals;
    }

    public byte[] SerializeQuestData() => UserSessionBinaryState.SerializeQuestData(
        Quest.QuestMap, Quest.QuestKillCountsMap, Quest.DailyCompletionDays);

    public void LoadQuestData(byte[] data)
    {
        UserSessionBinaryState.LoadQuestData(Quest.QuestMap, Quest.QuestKillCountsMap, data, Quest.DailyCompletionDays);
        Quest.SyncActiveQuestKillCounts();
    }

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

    public void InitExchange(bool start)
    {
        // Restore items/gold from exchange list
        foreach (var item in Trade.ExchangeItemList)
        {
            if (item.ItemId == InventoryConstants.ItemGold)
                Money += item.Count;
            else if (item.SrcPos < Inventory.Length)
                Inventory[item.SrcPos].Count += (ushort)item.Count;
        }
        Trade.ExchangeItemList.Clear();

        if (!start)
        {
            Trade.ExchangeUser = -1;
            Trade.ExchangeOk = false;
            Trade.AskedForExchange = false;
        }
    }

    public void CompleteExchange()
    {
        Trade.ExchangeItemList.Clear();
        Trade.ExchangeUser = -1;
        Trade.ExchangeOk = false;
        Trade.AskedForExchange = false;
    }
}
