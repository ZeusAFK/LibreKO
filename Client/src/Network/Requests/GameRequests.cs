namespace LibreKO.Network.Requests;

public sealed class GameRequests
{
    public IAchievementRequests Achievement { get; }
    public IAdminRequests Admin { get; }
    public IAttendanceRequests Attendance { get; }
    public IBattleEventRequests BattleEvent { get; }
    public IBifrostRequests Bifrost { get; }
    public IBountyRequests Bounty { get; }
    public ICapeRequests Cape { get; }
    public IChallengeRequests Challenge { get; }
    public IChangeHairRequests ChangeHair { get; }
    public ICharacterSealRequests CharacterSeal { get; }
    public IChatRequests Chat { get; }
    public IClanRequests Clan { get; }
    public IClanBattleWarRequests ClanBattleWar { get; }
    public IClanBoardsRequests ClanBoards { get; }
    public IClanPremiumRequests ClanPremium { get; }
    public IClanWarehouseRequests ClanWarehouse { get; }
    public IClientSettingsRequests ClientSettings { get; }
    public ICollectionRaceRequests CollectionRace { get; }
    public ICombatRequests Combat { get; }
    public ICorpseRequests Corpse { get; }
    public ICosmeticsRequests Cosmetics { get; }
    public IDailyRankRequests DailyRank { get; }
    public IDevelopRequests Develop { get; }
    public IEventScheduleRequests EventSchedule { get; }
    public IExchangeRequests Exchange { get; }
    public IExpSealRequests ExpSeal { get; }
    public IFishingHallRequests FishingHall { get; }
    public IForcesRequests Forces { get; }
    public IFriendsRequests Friends { get; }
    public IGenderChangeRequests GenderChange { get; }
    public IGenieRequests Genie { get; }
    public IGlobalMapRequests GlobalMap { get; }
    public IGuardPetRequests GuardPet { get; }
    public IItemCombineRequests ItemCombine { get; }
    public IItemSmashRequests ItemSmash { get; }
    public IItemUpgradeRequests ItemUpgrade { get; }
    public IItemsRequests Items { get; }
    public IKingRequests King { get; }
    public IKnightRoyaleRequests KnightRoyale { get; }
    public ILotteryRequests Lottery { get; }
    public IMailRequests Mail { get; }
    public IMarketPriceRequests MarketPrice { get; }
    public IMerchantRequests Merchant { get; }
    public IMerchantSearchRequests MerchantSearch { get; }
    public IMessengerRequests Messenger { get; }
    public IMiningRequests Mining { get; }
    public IMonsterStoneRequests MonsterStone { get; }
    public INameChangeRequests NameChange { get; }
    public INationChatRequests NationChat { get; }
    public INationTransferRequests NationTransfer { get; }
    public INearbyPlayersRequests NearbyPlayers { get; }
    public INpcQuestRequests NpcQuest { get; }
    public IObjectEventRequests ObjectEvent { get; }
    public IPartyRequests Party { get; }
    public IPartyWarBoardRequests PartyWarBoard { get; }
    public IPetRequests Pet { get; }
    public IPieceChangeRequests PieceChange { get; }
    public IPlayerRequests Player { get; }
    public IPlayerInspectRequests PlayerInspect { get; }
    public IPreGameRequests PreGame { get; }
    public IPremiumRequests Premium { get; }
    public IPresetRequests Preset { get; }
    public IProgressRequests Progress { get; }
    public IRankRequests Rank { get; }
    public IRankerMessageRequests RankerMessage { get; }
    public IRepairRequests Repair { get; }
    public IReportRequests Report { get; }
    public IRoomMediaRequests RoomMedia { get; }
    public IRouletteRequests Roulette { get; }
    public IShoppingMallRequests ShoppingMall { get; }
    public IShoutRequests Shout { get; }
    public ISiegeRequests Siege { get; }
    public ISoccerRequests Soccer { get; }
    public ISpecialAuctionRequests SpecialAuction { get; }
    public IStealthRequests Stealth { get; }
    public ITargetStateRequests TargetState { get; }
    public ITournamentRequests Tournament { get; }
    public IUserOptionRequests UserOption { get; }
    public IVanguardRequests Vanguard { get; }
    public IVendorRequests Vendor { get; }
    public IVipWarehouseRequests VipWarehouse { get; }
    public IWarehouseRequests Warehouse { get; }
    public IWarpRequests Warp { get; }
    public IZoneRequests Zone { get; }
    public IZoneConcurrentRequests ZoneConcurrent { get; }

    internal GameRequests(IPacketSink sink, GameSession session, LoginState login)
    {
        Achievement = new AchievementRequests(sink);
        Admin = new AdminRequests(sink);
        Attendance = new AttendanceRequests(sink);
        BattleEvent = new BattleEventRequests(sink);
        Bifrost = new BifrostRequests(sink);
        Bounty = new BountyRequests(sink);
        Cape = new CapeRequests(sink, session.Cape);
        Challenge = new ChallengeRequests(sink);
        ChangeHair = new ChangeHairRequests(sink, session.ChangeHair, session.Self);
        CharacterSeal = new CharacterSealRequests(sink);
        Chat = new ChatRequests(sink, session.Chat);
        Clan = new ClanRequests(sink);
        ClanBattleWar = new ClanBattleWarRequests(sink);
        ClanBoards = new ClanBoardsRequests(sink);
        ClanPremium = new ClanPremiumRequests(sink);
        ClanWarehouse = new ClanWarehouseRequests(sink);
        ClientSettings = new ClientSettingsRequests(sink);
        CollectionRace = new CollectionRaceRequests(sink);
        Combat = new CombatRequests(sink, session.Self, session.Genie, session.Combat);
        Corpse = new CorpseRequests(sink);
        Cosmetics = new CosmeticsRequests(sink);
        DailyRank = new DailyRankRequests(sink);
        Develop = new DevelopRequests(sink, session.Develop);
        EventSchedule = new EventScheduleRequests(sink);
        Exchange = new ExchangeRequests(sink);
        ExpSeal = new ExpSealRequests(sink);
        FishingHall = new FishingHallRequests(sink);
        Forces = new ForcesRequests(sink);
        Friends = new FriendsRequests(sink);
        GenderChange = new GenderChangeRequests(sink);
        Genie = new GenieRequests(sink);
        GlobalMap = new GlobalMapRequests(sink);
        GuardPet = new GuardPetRequests(sink);
        ItemCombine = new ItemCombineRequests(sink);
        ItemSmash = new ItemSmashRequests(sink);
        ItemUpgrade = new ItemUpgradeRequests(sink);
        Items = new ItemsRequests(sink, session.Items);
        King = new KingRequests(sink);
        KnightRoyale = new KnightRoyaleRequests(sink);
        Lottery = new LotteryRequests(sink);
        Mail = new MailRequests(sink);
        MarketPrice = new MarketPriceRequests(sink);
        Merchant = new MerchantRequests(sink);
        MerchantSearch = new MerchantSearchRequests(sink);
        Messenger = new MessengerRequests(sink);
        Mining = new MiningRequests(sink);
        MonsterStone = new MonsterStoneRequests(sink);
        NameChange = new NameChangeRequests(sink);
        NationChat = new NationChatRequests(sink, session.NationChat);
        NationTransfer = new NationTransferRequests(sink, session.NationTransfer);
        NearbyPlayers = new NearbyPlayersRequests(sink, session.NearbyPlayers);
        NpcQuest = new NpcQuestRequests(sink);
        ObjectEvent = new ObjectEventRequests(sink);
        Party = new PartyRequests(sink, session.Party, session.PartyWarBoard);
        PartyWarBoard = new PartyWarBoardRequests(sink, session.PartyWarBoard);
        Pet = new PetRequests(sink, session.Pet, session.Self, Combat);
        PieceChange = new PieceChangeRequests(sink);
        Player = new PlayerRequests(sink, session.Genie);
        PlayerInspect = new PlayerInspectRequests(sink);
        PreGame = new PreGameRequests(sink, session.Self, login);
        Premium = new PremiumRequests(sink);
        Preset = new PresetRequests(sink);
        Progress = new ProgressRequests(sink);
        Rank = new RankRequests(sink);
        RankerMessage = new RankerMessageRequests(sink);
        Repair = new RepairRequests(sink);
        Report = new ReportRequests(sink);
        RoomMedia = new RoomMediaRequests(sink, session.NationChat, session.RoomMedia);
        Roulette = new RouletteRequests(sink);
        ShoppingMall = new ShoppingMallRequests(sink);
        Shout = new ShoutRequests(sink);
        Siege = new SiegeRequests(sink, session.Siege);
        Soccer = new SoccerRequests(sink);
        SpecialAuction = new SpecialAuctionRequests(sink);
        Stealth = new StealthRequests(sink);
        TargetState = new TargetStateRequests(sink);
        Tournament = new TournamentRequests(sink);
        UserOption = new UserOptionRequests(sink, session.UserOption, session.Self);
        Vanguard = new VanguardRequests(sink);
        Vendor = new VendorRequests(sink);
        VipWarehouse = new VipWarehouseRequests(sink);
        Warehouse = new WarehouseRequests(sink);
        Warp = new WarpRequests(sink);
        Zone = new ZoneRequests(sink);
        ZoneConcurrent = new ZoneConcurrentRequests(sink);
    }
}
