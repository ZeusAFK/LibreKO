namespace LibreKO.Session;

public sealed class GameSession
{
    public AccountLockSession AccountLock { get; } = new();
    public AchievementSession Achievement { get; } = new();
    public AdminSession Admin { get; } = new();
    public AttendanceSession Attendance { get; } = new();
    public AwakenSession Awaken { get; } = new();
    public BattleEventSession BattleEvent { get; } = new();
    public BifrostSession Bifrost { get; } = new();
    public BountySession Bounty { get; } = new();
    public CapeSession Cape { get; } = new();
    public ChallengeSession Challenge { get; } = new();
    public ChangeHairSession ChangeHair { get; } = new();
    public ChaosSession Chaos { get; } = new();
    public CharacterSealSession CharacterSeal { get; } = new();
    public ChatSession Chat { get; } = new();
    public ClanSession Clan { get; } = new();
    public ClanBattleSession ClanBattle { get; } = new();
    public ClanBattleWarSession ClanBattleWar { get; } = new();
    public ClanBoardsSession ClanBoards { get; } = new();
    public ClanPremiumSession ClanPremium { get; } = new();
    public ClanWarehouseSession ClanWarehouse { get; } = new();
    public ClientSettingsSession ClientSettings { get; } = new();
    public CollectionRaceSession CollectionRace { get; } = new();
    public CombatSession Combat { get; } = new();
    public CorpseSession Corpse { get; } = new();
    public CosmeticsSession Cosmetics { get; } = new();
    public DailyRankSession DailyRank { get; } = new();
    public DevelopSession Develop { get; } = new();
    public DisguiseSession Disguise { get; } = new();
    public EffectSession Effect { get; } = new();
    public EntitiesSession Entities { get; } = new();
    public EventScheduleSession EventSchedule { get; } = new();
    public ExchangeSession Exchange { get; } = new();
    public ExpSealSession ExpSeal { get; } = new();
    public SlaveMerchantSession SlaveMerchant { get; } = new();
    public FishingHallSession FishingHall { get; } = new();
    public ForcesSession Forces { get; } = new();
    public FriendsSession Friends { get; } = new();
    public GenderChangeSession GenderChange { get; } = new();
    public GenieSession Genie { get; } = new();
    public GlobalMapSession GlobalMap { get; } = new();
    public GmFxSession GmFx { get; } = new();
    public GuardPetSession GuardPet { get; } = new();
    public ItemCombineSession ItemCombine { get; } = new();
    public ItemSmashSession ItemSmash { get; } = new();
    public ItemUpgradeSession ItemUpgrade { get; } = new();
    public ItemsSession Items { get; } = new();
    public KillAssistSession KillAssist { get; } = new();
    public KingSession King { get; } = new();
    public KnightRoyaleSession KnightRoyale { get; } = new();
    public LootSession Loot { get; } = new();
    public LotterySession Lottery { get; } = new();
    public MailSession Mail { get; } = new();
    public MarketPriceSession MarketPrice { get; } = new();
    public MerchantSession Merchant { get; } = new();
    public MerchantSearchSession MerchantSearch { get; } = new();
    public MessengerSession Messenger { get; } = new();
    public MiningSession Mining { get; } = new();
    public MonsterStoneSession MonsterStone { get; } = new();
    public NameChangeSession NameChange { get; } = new();
    public NationChatSession NationChat { get; } = new();
    public NationTransferSession NationTransfer { get; } = new();
    public NearbyPlayersSession NearbyPlayers { get; } = new();
    public NpcQuestSession NpcQuest { get; } = new();
    public ObjectEventSession ObjectEvent { get; } = new();
    public PartySession Party { get; }

    public SkillTimers SkillTimers { get; }

    public QuestBook QuestBook { get; } = new();
    public PartyWarBoardSession PartyWarBoard { get; } = new();
    public PetSession Pet { get; } = new();
    public PieceChangeSession PieceChange { get; } = new();
    public PlayerInspectSession PlayerInspect { get; } = new();
    public PreGameSession PreGame { get; } = new();
    public PremiumSession Premium { get; } = new();
    public PresetSession Preset { get; } = new();
    public ProgressSession Progress { get; }
    public PvpSession Pvp { get; } = new();
    public RankSession Rank { get; } = new();
    public RankerMessageSession RankerMessage { get; } = new();
    public RentalSession Rental { get; } = new();
    public RepairSession Repair { get; } = new();
    public ReportSession Report { get; } = new();
    public RoomMediaSession RoomMedia { get; } = new();
    public RouletteSession Roulette { get; } = new();
    public SeasonalSession Seasonal { get; } = new();
    public SelfSession Self { get; } = new();
    public ShoppingMallSession ShoppingMall { get; } = new();
    public ShoutSession Shout { get; } = new();
    public SiegeSession Siege { get; } = new();
    public SoccerSession Soccer { get; } = new();
    public SpecialAuctionSession SpecialAuction { get; } = new();
    public StealthSession Stealth { get; } = new();
    public TargetStateSession TargetState { get; } = new();
    public TournamentSession Tournament { get; } = new();
    public UpgradeNoticeSession UpgradeNotice { get; } = new();
    public UserOptionSession UserOption { get; }
    public VanguardSession Vanguard { get; } = new();
    public VendorSession Vendor { get; } = new();
    public VipWarehouseSession VipWarehouse { get; } = new();
    public WarehouseSession Warehouse { get; } = new();
    public WarpSession Warp { get; } = new();
    public ZoneSession Zone { get; } = new();
    public ZoneAbilitySession ZoneAbility { get; } = new();
    public ZoneConcurrentSession ZoneConcurrent { get; } = new();
    public HostilityRules Hostility { get; }

    public GameSession()
    {
        Progress = new ProgressSession(Self);
        Party = new PartySession(Self);
        SkillTimers = new SkillTimers(Self);
        UserOption = new UserOptionSession(Self);
        Hostility = new HostilityRules(Self, Siege, Clan, ClanBattleWar, ZoneAbility, Entities);
        Progress.StateChangeEvent += (charId, type, value) =>
        {
            if (type == StateChange.Stealth) Stealth.RaiseStealth(charId, (InvisibilityType)value);
            else if (type == Domain.Soccer.StateChangeTeam && charId == Self.MyCharId) Soccer.ApplyTeam(value);
        };
        Shout.ShoutUpgradeEvent += (ok, name, itemId, _) => UpgradeNotice.RaiseUpgradeNotice(ok, name, itemId);
    }

    internal void EntityEntered(EntitySnapshot e)
    {
        e.Attackable = e.IsNpc ? Hostility.IsHostileNpc(e) : Hostility.IsHostilePlayer(e);
        Entities.Add(e);
        Entities.RaiseEntitySpawn(e);
    }

    internal void TitleChanged(int charId, int titleId)
    {
        if (charId == Self.MyCharId) Achievement.ApplyOwnTitle(titleId);
        if (Entities.TryGet(charId, out var known)) known.TitleId = titleId;
        Achievement.RaiseTitleChanged(charId, titleId);
    }

    internal void EntityLeft(int id)
    {
        Entities.Remove(id);
        GmFx.Forget(id);
        UserOption.Forget(id);
        Merchant.ForgetStall(id);
        Entities.RaiseEntityOut(id);
    }

    internal void ResetCharacterState()
    {
        Self.ResetCharacterState();
        Progress.ResetOwnState();
        Clan.ForgetOnlineCount();
        Chaos.ResetBattle();
        Chaos.ForgetHotbar();
        Bifrost.ForgetFullMoonHotbar();
        Zone.ForgetGroundEffect();
        Pvp.ResetAnger();
        Chat.ApplyWhispersBlocked(false);
        Items.ForgetTimedItemsOffer();
        Soccer.LeaveTeam();
        NationChat.NationChat.Left();
        RoomMedia.Reset();
        ZoneAbility.ClearOwnStatus();
        Party.Clear();
        Friends.Clear();
        SlaveMerchant.Clear();
    }

    internal void ResetPendingOperations()
    {
        Genie.Reset();
        Items.CancelPendingRequests();
        Cape.ApplyReset();
        Pet.Reset();
        Develop.ApplyRebirthReset();
        ChangeHair.CancelPending();
        NationTransfer.CancelPending();
        Vendor.EndTrade();
    }
}
