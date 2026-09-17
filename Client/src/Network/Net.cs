using System;
using System.Collections.Generic;
using Godot;

namespace LibreKO.Network;

public partial class Net : Node
{
    public static Net I { get; private set; } = null!;

    public event Action? ConnectedEvent;
    public event Action<string>? ErrorEvent;
    public event Action<int>? VersionEvent;
    public event Action<bool, int>? LoginResultEvent;
    public event Action<int>? NationResultEvent;
    public event Action<int>? CreateCharResultEvent;
    public event Action<List<CharacterSummary>>? CharListEvent;
    public event Action<MyInfo>? EnterWorldEvent;

    public event Action<EntitySnapshot>? EntitySpawnEvent;
    public event Action<int, float, float, float, float, bool>? EntityMoveEvent;
    public event Action<int, float>? EntityRotateEvent;
    public event Action<int>? EntityOutEvent;
    public event Action<int, int, int, int>? EntityHpEvent;
    public event Action<int, int, int>? EntityHpSyncEvent;

    public event Action<int, int, int, int>? AttackEvent;
    public event Action<int, int, int, int, short[]>? MagicEvent;
    public event Action<int, int>? DeadEvent;
    public event Action<int, int, int>? SelfHpEvent;
    public event Action<int, int>? SelfMpEvent;
    public event Action<float, float>? RegeneEvent;
    public event Action<int[]>? SkillDataEvent;
    public event Action? SkillBarClearEvent;

    public event Action<bool>? ItemMoveResultEvent;
    public event Action<DerivedStats>? ItemStatsEvent;
    public event Action<int, ItemSlot>? InventorySlotEvent;
    public event Action<int, int>? ItemGainedEvent;
    public event Action<ItemSlot[]>? InventoryGridRefreshEvent;
    public event Action<bool>? ItemRemoveResultEvent;
    public event Action<int, int, int, short>? LookChangeEvent;

    public event Action<int, int, int, int, int, int>? PointChangeEvent;
    public event Action<int>? GoldChangeEvent;
    public event Action<long>? ExpChangeEvent;
    public event Action<long>? DeathExpLossEvent;

    public event Action<int, int>? TimeEvent;
    public event Action<int, int>? WeatherEvent;
    public event Action<string>? NoticeEvent;
    public event Action<float, float>? WarpEvent;

    private readonly Dictionary<int, EntitySnapshot> _known = new();

    public bool IsKnownAttackable(int id) =>
        _known.TryGetValue(id, out var known) && known.Attackable;

    private void RaiseSpawn(EntitySnapshot e)
    {
        e.Attackable = e.IsNpc ? IsHostileNpc(e) : IsHostilePlayer(e);
        _known[e.Id] = e;
        EntitySpawnEvent?.Invoke(e);
    }

    private void RaiseOut(int id)
    {
        _known.Remove(id);
        _gmFxStates.Remove(id);
        _knownStalls.Remove(id);
        EntityOutEvent?.Invoke(id);
    }

    public void ReplayKnownEntities(Action<EntitySnapshot> onSpawn)
    {
        foreach (var e in _known.Values)
            onSpawn(e);
    }

    private readonly KoConn _conn = new();
    private bool _connectedFired;
    private bool _connectFailReported;
    private bool _expectedClose;
    private string _account = "";
    private string _password = "";
    private bool _autoLoginPending;

    private const double PingIntervalSeconds = 1.0;
    private const ulong PingTimeoutMs = 5000;
    private double _pingAccum;
    private ulong _pingSentMs;
    private bool _pingOutstanding;

    public int ServerVersion { get; private set; }
    public int MyCharId { get; private set; }
    public int Nation { get; private set; }
    public string SelectedChar { get; private set; } = "";
    public MyInfo LastEnter { get; private set; }

    internal void SeedPreviewEnter(MyInfo info) => LastEnter = info;

    public int PingMs { get; private set; } = -1;

    public bool Connected => _conn.Connected;

    public override void _Ready()
    {
        I = this;
        Config.Load();
    }

    public void BeginGameLogin(string host, int port, string account, string password)
    {
        _account = account;
        _password = password;
        _autoLoginPending = true;
        ConnectToServer(host, port);
    }

    public void ConnectToServer(string host, int port)
    {
        _host = host;
        _port = port;
        _connectedFired = false;
        _expectedClose = false;
        Evicted = false;
        AutoReconnect = true;
        _reconnectKickSent = false;
        MyCharId = 0;
        PingMs = -1;
        _pingOutstanding = false;
        _pingAccum = 0;
        _missedPings = 0;
        _connectFailReported = false;
        CancelReconnect();
        _conn.Connect(host, port);
    }

    public void Disconnect(bool expected = false)
    {
        CancelReconnect();
        _expectedClose = expected;
        if (_conn.Connected && MyCharId != 0)
        {
            var logout = new Packet(GameOpcodes.GS_LOGOUT);
            logout.WriteByte(0);
            _conn.Send(logout);
        }
        _conn.Close();
        PingMs = -1;
        _pingOutstanding = false;
    }

    public void ReturnToCharSelect()
    {
        CancelReconnect();
        if (_conn.Connected && MyCharId != 0)
        {
            var logout = new Packet(GameOpcodes.GS_LOGOUT);
            logout.WriteByte(0);
            _conn.Send(logout);
        }
        MyCharId = 0;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest || what == NotificationPredelete)
            Disconnect(expected: true);
        if (what == NotificationWMCloseRequest)
        {
            try { LoginNet.I?.Disconnect(); } catch { }
            OS.Kill(OS.GetProcessId());
        }
    }

    public override void _Process(double delta)
    {
        if (_conn.Connected && !_connectedFired)
        {
            _connectedFired = true;
            ConnectedEvent?.Invoke();
            SendVersionCheck();
        }
        if (!_conn.Connected && _connectedFired)
        {
            _connectedFired = false;
            PingMs = -1;
            _pingOutstanding = false;
            if (!_expectedClose && !Evicted && !TakeOverDisconnect())
                ErrorEvent?.Invoke(_conn.LastError ?? "disconnected");
            _expectedClose = false;
        }
        if (_conn.ConnectFailed && !_connectFailReported)
        {
            _connectFailReported = true;
            if (!TakeOverDisconnect())
                ErrorEvent?.Invoke(_conn.LastError ?? "connection failed");
        }
        while (_conn.Incoming.TryDequeue(out var p))
        {
            try { Handle(p); }
            catch (Exception e) { Diag.Report($"packet 0x{p.GetOpcode():X2}", e); }
        }

        SchedulePing(delta);
        ReconnectTick(delta);
    }

    private void SchedulePing(double delta)
    {
        if (!_conn.Connected || !Config.PingEnabled)
            return;

        ulong now = Time.GetTicksMsec();
        if (_pingOutstanding && now - _pingSentMs > PingTimeoutMs)
        {
            _pingOutstanding = false;
            PingMs = -1;
            PingLost();
        }

        _pingAccum += delta;
        if (_pingAccum >= PingIntervalSeconds && !_pingOutstanding)
        {
            _pingAccum = 0;
            _pingSentMs = now;
            _pingOutstanding = true;
            SendPing();
        }
    }

    private void HandlePing(Packet p)
    {
        if (p.RemainingBytes >= sizeof(long))
        {
            PingMs = (int)((long)Time.GetTicksMsec() - p.ReadLong());
            _pingOutstanding = false;
            _missedPings = 0;
        }
    }

    internal void Handle(Packet p)
    {
        var op = (GameOpcodes)p.GetOpcode();
        switch (op)
        {
            case GameOpcodes.GS_VERSION_CHECK:    HandleVersionCheck(p); break;
            case GameOpcodes.GS_PING:             HandlePing(p); break;
            case GameOpcodes.GS_LOGIN:            HandleLogin(p); break;
            case GameOpcodes.GS_KICKOUT:          HandleKickOut(p); break;
            case GameOpcodes.GS_NATION_SELECT:    HandleNationSelect(p); break;
            case GameOpcodes.GS_CREATE_CHARACTER: HandleCreateCharacter(p); break;
            case GameOpcodes.GS_ALLCHAR_INFO_REQ: ParseCharList(p); break;
            case GameOpcodes.GS_SELECT_CHARACTER: ParseSelect(p); break;
            case GameOpcodes.GS_MYINFO:           ParseMyInfo(p); break;

            case GameOpcodes.GS_MOVE:         HandleMove(p); break;
            case GameOpcodes.GS_ROTATE:       HandleRotate(p); break;
            case GameOpcodes.GS_WARP:         HandleWarp(p); break;
            case GameOpcodes.GS_ZONE_CHANGE:  HandleZoneChange(p); break;
            case GameOpcodes.GS_USER_INOUT:   HandleUserInOut(p); break;
            case GameOpcodes.GS_TIME:         HandleTime(p); break;
            case GameOpcodes.GS_WEATHER:      HandleWeather(p); break;
            case GameOpcodes.GS_NOTICE:       HandleNotice(p); break;

            case GameOpcodes.GS_SKILLPT_CHANGE: HandleSkillPointChange(p); break;
            case GameOpcodes.GS_CLASS_CHANGE:   HandleClassChange(p); break;
            case GameOpcodes.GS_REGIONCHANGE: ParseRegionUserList(p); break;
            case GameOpcodes.GS_REQ_USERIN:   ParseUserSnapshot(p); break;
            case GameOpcodes.GS_NPC_REGION:   ParseNpcRegionList(p); break;
            case GameOpcodes.GS_REQ_NPCIN:    ParseNpcSnapshot(p); break;
            case GameOpcodes.GS_NPC_INOUT:    HandleNpcInOut(p); break;
            case GameOpcodes.GS_NPC_MOVE:     HandleNpcMove(p); break;

            case GameOpcodes.GS_TARGET_HP:     HandleTargetHp(p); break;
            case GameOpcodes.GS_ATTACK:        HandleAttack(p); break;
            case GameOpcodes.GS_MAGIC_PROCESS: HandleMagicProcess(p); break;
            case GameOpcodes.GS_DEAD:          HandleDead(p); break;
            case GameOpcodes.GS_HP_CHANGE:     HandleHpChange(p); break;
            case GameOpcodes.GS_MSP_CHANGE:    HandleMspChange(p); break;
            case GameOpcodes.GS_REGENE:        HandleRegene(p); break;
            case GameOpcodes.GS_SKILLDATA:     HandleSkillData(p); break;

            case GameOpcodes.GS_ITEM_MOVE:         HandleItemMove(p); break;
            case GameOpcodes.GS_ITEM_COUNT_CHANGE: HandleItemCountChange(p); break;
            case GameOpcodes.GS_ITEM_GET:          HandleItemGet(p); break;

            case GameOpcodes.GS_ITEM_DROP:         HandleItemDrop(p); break;
            case GameOpcodes.GS_BUNDLE_OPEN_REQ:   HandleBundleOpen(p); break;
            case GameOpcodes.GS_ITEM_REMOVE:       HandleItemRemove(p); break;
            case GameOpcodes.GS_USERLOOK_CHANGE:   HandleUserLookChange(p); break;
            case GameOpcodes.GS_POINT_CHANGE:      HandlePointChange(p); break;
            case GameOpcodes.GS_GOLD_CHANGE:       HandleGoldChange(p); break;
            case GameOpcodes.GS_EXP_CHANGE:        HandleExpChange(p); break;

            case GameOpcodes.GS_CHAT:              HandleChat(p); break;
            case GameOpcodes.GS_CHAT_TARGET:       HandleChatTarget(p); break;

            case GameOpcodes.GS_PARTY:             HandleParty(p); break;
            case GameOpcodes.GS_PARTY_BBS:         HandlePartyBbs(p); break;

            case GameOpcodes.GS_FRIEND_PROCESS:    HandleFriendProcess(p); break;

            case GameOpcodes.GS_SELECT_MSG:        HandleSelectMsg(p); break;
            case GameOpcodes.GS_NPC_SAY:           HandleNpcSay(p); break;
            case GameOpcodes.GS_QUEST:             HandleQuest(p); break;

            case GameOpcodes.GS_TRADE_NPC:         HandleTradeNpc(p); break;
            case GameOpcodes.GS_ITEM_TRADE:        HandleItemTrade(p); break;

            case GameOpcodes.GS_EXCHANGE:          HandleExchange(p); break;

            case GameOpcodes.GS_MINING:            HandleMining(p); break;
            case GameOpcodes.GS_DURATION:          HandleDuration(p); break;

            case GameOpcodes.GS_MERCHANT:          HandleMerchant(p); break;
            case GameOpcodes.GS_MERCHANT_INOUT:    HandleMerchantInOut(p); break;

            case GameOpcodes.GS_LEVEL_CHANGE:      HandleLevelChange(p); break;
            case GameOpcodes.GS_LOYALTY_CHANGE:    HandleLoyaltyChange(p); break;
            case GameOpcodes.GS_WEIGHT_CHANGE:     HandleWeightChange(p); break;
            case GameOpcodes.GS_STATE_CHANGE:      HandleStateChange(p); break;
            case GameOpcodes.GS_STEALTH:           HandleStealth(p); break;

            case GameOpcodes.GS_ITEM_UPGRADE:      HandleItemUpgrade(p); break;

            case GameOpcodes.GS_REPAIR_NPC:        HandleRepairNpc(p); break;
            case GameOpcodes.GS_ITEM_REPAIR:       HandleItemRepair(p); break;

            case GameOpcodes.GS_WAREHOUSE:         HandleWarehouse(p); break;

            case GameOpcodes.GS_KNIGHTS_PROCESS:   HandleKnights(p); break;
            case GameOpcodes.GS_KNIGHTS_LIST:      HandleKnightsList(p); break;

            case GameOpcodes.GS_HELMET:            HandleHelmet(p); break;
            case GameOpcodes.GS_PREMIUM:           HandlePremium(p); break;
            case GameOpcodes.GS_SANTA:             HandleSanta(p); break;
            case GameOpcodes.GS_ZONEABILITY:       HandleZoneAbility(p); break;
            case GameOpcodes.GS_LOGOSSHOUT:        HandleShout(p); break;
            case GameOpcodes.GS_RANK:              HandleRank(p); break;
            case GameOpcodes.GS_CHALLENGE:         HandleChallenge(p); break;
            case GameOpcodes.GS_WARP_LIST:         HandleWarpList(p); break;
            case GameOpcodes.GS_SHOPPING_MALL:     HandleShoppingMall(p); break;
            case GameOpcodes.GS_BATTLE_EVENT:      HandleBattleEvent(p); break;
            case GameOpcodes.GS_MAP_EVENT:         HandleMapEvent(p); break;
            case GameOpcodes.GS_PET:               HandlePet(p); break;

            case GameOpcodes.GS_REBIRTH:           HandleRebirth(p); break;
            case GameOpcodes.GS_CORPSE:            HandleCorpse(p); break;
            case GameOpcodes.GS_BIFROST:           HandleBifrost(p); break;
            case GameOpcodes.GS_EVENT:             HandleBifrostEvent(p); break;
            case GameOpcodes.GS_USER_INFO:         HandleUserInfoOpcode(p); break;
            case GameOpcodes.GS_CAPE:              HandleCape(p); break;
            case GameOpcodes.GS_NAME_CHANGE:       HandleNameChange(p); break;
            case GameOpcodes.GS_KING:              HandleKing(p); break;
            case GameOpcodes.GS_SIEGE:             HandleSiege(p); break;
            case GameOpcodes.GS_PVP:               HandlePvp(p); break;

            case GameOpcodes.GS_CLAN_WAREHOUSE:    HandleClanWarehouse(p); break;
            case GameOpcodes.GS_VIP_WAREHOUSE:     HandleVipWarehouse(p); break;
            case GameOpcodes.GS_CLAN_BATTLE:       HandleClanBattle(p); break;
            case GameOpcodes.GS_CLANPOINTS_BATTLE: HandleClanBattlePoints(p); break;
            case GameOpcodes.GS_CLAN_PREMIUM:      HandleClanPremium(p); break;
            case GameOpcodes.GS_OBJECT_EVENT:      HandleObjectEvent(p); break;
            case GameOpcodes.GS_REPORT:            HandleReport(p); break;
            case GameOpcodes.GS_ZONE_CONCURRENT:   HandleZoneConcurrent(p); break;

            case GameOpcodes.GS_AWAKEN:            HandleAwaken(p); break;
            case GameOpcodes.GS_CHANGE_HAIR:       HandleChangeHair(p); break;

            case GameOpcodes.GS_ACHIEVEMENT:       HandleAchievement(p); break;
            case GameOpcodes.GS_MAIL:              HandleMail(p); break;
            case GameOpcodes.GS_AUCTION:           HandleAuction(p); break;
            case GameOpcodes.GS_EVENT_BOARD:       HandleEventBoard(p); break;
            case GameOpcodes.GS_BOUNTY:            HandleBounty(p); break;
            case GameOpcodes.GS_TOURNAMENT:        HandleTournament(p); break;
            case GameOpcodes.GS_DISGUISE:          HandleDisguise(p); break;
            case GameOpcodes.GS_PRESET:            HandlePreset(p); break;
            case GameOpcodes.GS_MESSENGER:         HandleMessenger(p); break;
            case GameOpcodes.GS_FORCES:            HandleForces(p); break;
            case GameOpcodes.GS_INSTANCE:          HandleInstance(p); break;
            case GameOpcodes.GS_CHATROOM:          HandleChatRoom(p); break;
            case GameOpcodes.GS_NATION_TAX:        HandleNationTax(p); break;
            case GameOpcodes.GS_FORTUNE:           HandleFortune(p); break;
            case GameOpcodes.GS_ITEM_COMBINE:      HandleItemCombine(p); break;
            case GameOpcodes.GS_FISHING_HALL:      HandleFishingHall(p); break;
            case GameOpcodes.GS_MARKET_BBS:        HandleMarketBbs(p); break;
            case GameOpcodes.GS_DUEL:              HandleDuel(p); break;
            case GameOpcodes.GS_ITEM_EXCHANGE:     HandleItemExchange(p); break;
            case GameOpcodes.GS_RING_UPGRADE:      HandleRingUpgrade(p); break;
            case GameOpcodes.GS_INN:               HandleInn(p); break;
            case GameOpcodes.GS_GUARD_PET:         HandleGuardPet(p); break;
            case GameOpcodes.GS_EVENT_QUEST:       HandleEventQuest(p); break;
            case GameOpcodes.GS_GLOBAL_MAP:        HandleGlobalMap(p); break;
            case GameOpcodes.GS_GENIE:             HandleGenie(p); break;
            case GameOpcodes.GS_CLIENT_SETTINGS:   HandleClientSettings(p); break;
            case GameOpcodes.GS_DAILY_QUEST:       HandleDailyQuest(p); break;
            case GameOpcodes.GS_RENTAL:            HandleRental(p); break;
            case GameOpcodes.GS_ADMIN_PANEL:       HandleAdminPanel(p); break;
        }
    }

    private static void SkipBytes(Packet p, int n)
    {
        int take = Math.Min(n, p.RemainingBytes);
        if (take > 0) p.ReadBytes(take);
    }

    private static short UShortToShort(ushort value) => value > short.MaxValue ? short.MaxValue : (short)value;
    private static short IntToShort(int value) => value > short.MaxValue ? short.MaxValue : value < short.MinValue ? short.MinValue : (short)value;
}
