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
    public SemaphoreSlim PacketGate { get; } = new(1, 1);

    private int closing;
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // A session closes once; whoever comes second waits for the first close to finish.
    public bool TryBeginClosing() => Interlocked.CompareExchange(ref closing, 1, 0) == 0;
    public bool IsClosing => Volatile.Read(ref closing) == 1;
    public Task Closed => closed.Task;
    public void MarkClosed() => closed.TrySetResult();

    private volatile bool savesSealed;

    // After the final save nothing more is written for this session.
    public bool SavesSealed => savesSealed;

    public void SealSaves() => savesSealed = true;

    private int gameStarted;

    public bool TryBeginGame() => Interlocked.CompareExchange(ref gameStarted, 1, 0) == 0;

    private const int ClientSaveCooldownSeconds = 10;
    public static readonly TimeSpan ClientSaveCooldown = TimeSpan.FromSeconds(ClientSaveCooldownSeconds);
    private long lastClientSaveTicks;

    // A save the client asks for runs at most once per cooldown; now is monotonic time since boot.
    public bool TryTakeClientSave(TimeSpan now)
    {
        var last = Interlocked.Read(ref lastClientSaveTicks);
        return (last == 0 || now.Ticks - last >= ClientSaveCooldown.Ticks)
            && Interlocked.CompareExchange(ref lastClientSaveTicks, now.Ticks, last) == last;
    }

    public static TimeSpan SinceBoot => TimeSpan.FromMilliseconds(Environment.TickCount64);
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
    public long LastMoveTicks { get; set; }
    public float MoveBudget { get; set; }
    public float LastStepSpeed { get; set; }
    public float LastStepDistance { get; set; }
    public long HoldSinceTicks { get; private set; }
    public long HoldUntilTicks { get; private set; }
    public long MoveBudgetTicks { get; set; }
    public long PlacedTicks { get; set; }
    public HashSet<short> OfferedWarps { get; } = [];
    public float OfferedWarpsX { get; set; }
    public float OfferedWarpsZ { get; set; }
    public byte MoveOldEcho { get; set; }

    // Set when this player moved; the movement-broadcast service coalesces the GS_MOVE
    // fan-out to a fixed cadence (instead of one broadcast per received move packet).
    public volatile bool MovePending;

    // Derived stats
    public short MaxHp { get; set; }
    private short hp;
    private int deathRecorded;

    public short Hp
    {
        get => hp;
        set
        {
            hp = value;
            if (value > 0 && Volatile.Read(ref deathRecorded) != 0)
                Interlocked.Exchange(ref deathRecorded, 0);
        }
    }

    // One death per life: a second blow on the corpse records nothing.
    public bool TryRecordDeath() => Interlocked.CompareExchange(ref deathRecorded, 1, 0) == 0;
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

    // Every item-moving handler refuses while this holds: a trade or a stall may count on the bag.
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
    public DateTime? GenieRewardClaimedOn { get; set; }
    public int WarehouseKeeperId { get; set; }

    // Rebirth (WIZ_REBIRTH 0xD3). Snapshot stats stored at rebirth time.
    public short RebirthLevel { get; set; }
    public byte RebStr { get; set; }
    public byte RebSta { get; set; }
    public byte RebDex { get; set; }
    public byte RebIntel { get; set; }
    public byte RebMagic { get; set; }

    // Party
    private int partyIndex = -1;

    public int PartyIndex
    {
        get => Volatile.Read(ref partyIndex);
        set => Volatile.Write(ref partyIndex, value);
    }

    public bool IsInParty => PartyIndex >= 0;

    // A player starts a party only from outside every party.
    public bool TryStartParty(int index) => Interlocked.CompareExchange(ref partyIndex, index, -1) == -1;

    private int invitedToParty = -1;

    public int InvitedToParty
    {
        get => Volatile.Read(ref invitedToParty);
        set => Volatile.Write(ref invitedToParty, value);
    }

    // An invite stands until it is answered or its party is gone.
    public bool TryOfferPartyInvite(int index, Func<int, bool> partyStands)
    {
        var pending = InvitedToParty;
        return (pending < 0 || !partyStands(pending))
            && Interlocked.CompareExchange(ref invitedToParty, index, pending) == pending;
    }

    public int TakePartyInvite() => Interlocked.Exchange(ref invitedToParty, -1);

    private const int MaxBoardInvites = PartyGroup.MaxMembers;
    private readonly List<PartyBoardInvite> boardInvites = [];

    // Several board invites wait at once; past the cap the oldest makes way.
    public void OfferBoardInvite(PartyBoardInvite invite)
    {
        lock (boardInvites)
        {
            boardInvites.RemoveAll(pending => pending.InviterId == invite.InviterId);
            if (boardInvites.Count >= MaxBoardInvites)
                boardInvites.RemoveAt(0);
            boardInvites.Add(invite);
        }
    }

    public bool TryTakeBoardInvite(PartyBoardInvite answer)
    {
        lock (boardInvites)
            return boardInvites.Remove(answer);
    }

    public void ForgetBoardInvites()
    {
        lock (boardInvites)
            boardInvites.Clear();
    }

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
    public long InstantSkillReadyTicks { get; set; }
    public long NextSwingTicks { get; set; }
    public long SwingIntervalTicks { get; set; }
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
    public bool IsHeld => activeBuffs.Any(entry => entry.Value.BuffType is BuffType.Freeze or BuffType.Stun && !entry.Value.IsExpired);
    public bool CarriesManes => activeBuffs.Any(entry => entry.Value.BuffType == BuffType.FragmentOfManes && !entry.Value.IsExpired);

    // Another player's call moves this one only while free, not under NoRecall or the Fragment, and not in Prison.
    public bool MayBeMovedByOthers =>
        !IsBusy && CanTeleport && !CarriesManes && !IsWarping && ZoneId != (byte)LibreKO.Common.Enums.ZoneId.Prison;
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
    private const int WarpingTimeoutSeconds = 60;
    public long WarpingUntilTicks { get; set; }

    public bool IsWarping
    {
        get => WarpingUntilTicks != 0 && DateTime.UtcNow.Ticks < WarpingUntilTicks;
        set => WarpingUntilTicks = value ? DateTime.UtcNow.AddSeconds(WarpingTimeoutSeconds).Ticks : 0;
    }
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
        if (buff.BuffType is BuffType.Freeze or BuffType.Stun)
        {
            var start = buff.ExpireTicks - buff.Duration * TimeSpan.TicksPerSecond;
            if (HoldUntilTicks <= start)
                HoldSinceTicks = start;
            HoldUntilTicks = Math.Max(HoldUntilTicks, buff.ExpireTicks);
        }

        activeBuffs[skillId] = buff;
        BuffsChanged?.Invoke(this);
    }

    public bool RemoveBuff(int skillId) => RemoveBuff(skillId, out _);

    public bool RemoveBuff(int skillId, [MaybeNullWhen(false)] out ActiveBuff removed)
    {
        if (!activeBuffs.TryRemove(skillId, out removed))
            return false;

        if (removed.BuffType is BuffType.Freeze or BuffType.Stun)
            ForgetEndedHold();
        BuffsChanged?.Invoke(this);
        return true;
    }

    public void ReplaceBuff(int replacedSkillId, int skillId, ActiveBuff buff)
    {
        activeBuffs.TryRemove(replacedSkillId, out _);
        SetBuff(skillId, buff);
    }

    private void ForgetEndedHold()
    {
        if (activeBuffs.Values.Any(buff => buff.BuffType is BuffType.Freeze or BuffType.Stun))
            return;
        HoldSinceTicks = 0;
        HoldUntilTicks = 0;
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
        {
            ForgetEndedHold();
            BuffsChanged?.Invoke(this);
        }
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
                if (Inventory[i].ItemId == itemId && Inventory[i].IsPlain
                    && Inventory[i].Count + count <= InventoryConstants.MaxStackCount)
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

    // Call under this session's lock.
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
        Trade.ExchangeOk = false;
        Trade.ExchangeAccepted = start;

        if (!start)
        {
            Trade.ExchangeUser = -1;
            Trade.AskedForExchange = false;
        }

        return returned;
    }

    // Its own slot, else a free one, else the warehouse.
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
        if (returned != null)
            returned.ItemsToWarehouse++;
        return true;
    }

    // Hands back the gold a buying stall still holds, the same way an offer's gold comes back.
    public (int ToPurse, int ToWarehouse, long Lost) ReleaseBuyMerchantGold() => WithLock(s =>
    {
        var held = s.Trade.BuyMerchantGold;
        s.Trade.BuyMerchantGold = 0;
        return s.ReturnHeldGold(held);
    });

    // Gold a stall or a trade holds aside is saved as if it had come back.
    public int SavedMoney => WithLock(s => s.Money + s.SplitHeldGold(s.HeldGold).ToPurse);

    public int SavedWarehouseMoney => WithLock(s => s.WarehouseMoney + s.SplitHeldGold(s.HeldGold).ToWarehouse);

    public byte[] SavedItems => WithLock(s => UserSessionBinaryState.SerializeItems(s.AsIfOffersCameBack().Bag));

    public byte[] SavedWarehouse => WithLock(s => UserSessionBinaryState.SerializeWarehouse(s.AsIfOffersCameBack().Store));

    private long HeldGold => Trade.BuyMerchantGold
        + Trade.ExchangeItemList.Where(item => item.ItemId == InventoryConstants.ItemGold).Sum(item => (long)item.Count);

    private (ItemSlot[] Bag, ItemSlot[] Store) AsIfOffersCameBack()
    {
        var bag = Copies(Inventory);
        var store = Copies(Warehouse);
        foreach (var item in Trade.ExchangeItemList.Where(item => item.ItemId != InventoryConstants.ItemGold))
        {
            var slot = ItemPlacement.FindForReturn(bag, InventoryConstants.InventoryStart, InventoryConstants.HaveMax, item, item.SrcPos);
            if (slot >= 0)
            {
                ItemPlacement.Put(bag[slot], item);
                continue;
            }

            slot = ItemPlacement.FindForReturn(store, 0, WarehouseMax, item);
            if (slot >= 0)
                ItemPlacement.Put(store[slot], item);
        }

        return (bag, store);
    }

    private static ItemSlot[] Copies(ItemSlot[] slots)
    {
        var copies = new ItemSlot[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            copies[i] = new ItemSlot();
            InventoryJournal.CopyInto(slots[i], copies[i]);
        }

        return copies;
    }

    // Held gold goes into the purse up to the coin cap and the rest into the warehouse.
    public (int ToPurse, int ToWarehouse, long Lost) ReturnHeldGold(long amount)
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
        Trade.ExchangeAccepted = false;
        Trade.AskedForExchange = false;
    }
}
