using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class PacketReadersTests
{
    private const byte BattleZoneSeen = 62;

    private sealed class NoConnection : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    private static (PacketDispatcher Dispatcher, GameSession Session) Readers()
    {
        var session = new GameSession();
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new NoConnection(), session, new LoginState()), () => 0);
        return (dispatcher, session);
    }

    private static Packet EventBoard(EventBoardSubOpcode sub, params int[] values)
    {
        var p = new Packet(GameOpcodes.GS_EVENT_BOARD);
        p.WriteByte((byte)sub);
        foreach (int value in values) p.WriteInt(value);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void EveryReaderTakesItsOwnOpcodes()
    {
        var (dispatcher, _) = Readers();

        Assert.True(dispatcher.Handles(GameOpcodes.GS_MAIL));
        Assert.True(dispatcher.Handles(GameOpcodes.GS_EVENT_BOARD));
    }

    [Fact]
    public void TheRoulettePrizeListReachesTheSession()
    {
        var (dispatcher, session) = Readers();
        RoulettePrize[]? log = null;
        session.Roulette.RoulettePrizeLogEvent += entries => log = entries;

        dispatcher.Dispatch(EventBoard(EventBoardSubOpcode.RoulettePrizeList, 1, 1, 1, 389_010_000, 2, 1_760_000_000));

        Assert.NotNull(log);
        Assert.Equal([new RoulettePrize(389_010_000, 2, 1_760_000_000)], log);
    }

    private static Packet Incoming(GameOpcodes opcode, params byte[] body)
    {
        var p = new Packet(opcode);
        foreach (byte b in body) p.WriteByte(b);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void AnItemUpgradeSubOpcodeNoReaderTakesOpensTheNpcWindow()
    {
        var (dispatcher, session) = Readers();
        GameOpcodes? window = null;
        session.NpcQuest.NpcWindowEvent += opcode => window = opcode;

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_ITEM_UPGRADE, 99));

        Assert.Equal(GameOpcodes.GS_ITEM_UPGRADE, window);
    }

    [Fact]
    public void AGmEffectOnTheAdminPanelReachesTheGmFxSession()
    {
        var (dispatcher, session) = Readers();
        (int, bool)? effect = null;
        session.GmFx.GmFxEvent += (id, enabled) => effect = (id, enabled);
        var p = new Packet(GameOpcodes.GS_ADMIN_PANEL);
        p.WriteByte((byte)AdminReplySubOpcode.GmFx);
        p.WriteInt(1234);
        p.WriteByte(1);
        p.ResetOffset();

        dispatcher.Dispatch(p);

        Assert.Equal((1234, true), effect);
    }

    [Fact]
    public void TheMerchantOfficialListReachesMerchantSearch()
    {
        var (dispatcher, session) = Readers();
        bool opened = false;
        session.MerchantSearch.MerchantSearchOpenEvent += () => opened = true;

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_MERCHANT, (byte)MerchantSubOpcode.OfficialList,
            (byte)MerchantSearchSubOpcode.Open, (byte)MerchantSearchResult.Accepted));

        Assert.True(opened);
    }

    [Fact]
    public void AClanBattleStartOnTheKnightsOpcodeReachesTheClanBattleSession()
    {
        var (dispatcher, session) = Readers();
        short? enemy = null;
        session.ClanBattleWar.ClanBattleBegunEvent += clanId => enemy = clanId;
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.ClanBattleBegun);
        p.WriteShort(321);
        p.ResetOffset();

        dispatcher.Dispatch(p);

        Assert.Equal((short)321, enemy);
    }

    [Fact]
    public void AClanResultStillReachesTheClanSession()
    {
        var (dispatcher, session) = Readers();
        bool raised = false;
        session.Clan.ClanResultEvent += (_, _) => raised = true;

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_KNIGHTS_PROCESS, (byte)KnightsSubOpcode.Join, (byte)KnightsResult.ClanFull));

        Assert.True(raised);
    }

    [Fact]
    public void TheNearbyListOnTheUserInfoOpcodeReachesNearbyPlayers()
    {
        var (dispatcher, session) = Readers();
        List<NearbyPlayer>? listed = null;
        session.NearbyPlayers.NearbyPlayersEvent += list => listed = list;

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_USER_INFO, (byte)PlayerInspectSubOpcode.Nearby, 0, 0, 0, 0, 0, 0));

        Assert.NotNull(listed);
        Assert.Empty(listed);
    }

    [Fact]
    public void ATempleEventDisbandStillReachesBifrost()
    {
        var (dispatcher, session) = Readers();
        bool disbanded = false;
        session.Bifrost.BifrostDisbandEvent += () => disbanded = true;

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_EVENT, (byte)TempleSubOpcode.TempleEventDisband, 1, 0, 0));

        Assert.True(disbanded);
    }

    [Fact]
    public void TheSoccerClockOnTheMiningOpcodeReachesTheSoccerSession()
    {
        var (dispatcher, session) = Readers();
        int? clock = null;
        session.Soccer.SoccerClockEvent += seconds => clock = seconds;

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_MINING, (byte)MiningSubOpcode.Soccer, (byte)SoccerSubOpcode.Clock, 90, 0));

        Assert.Equal(90, clock);
    }

    [Fact]
    public void AnAttendanceFailureCarriesItsSubOpcodeAndResult()
    {
        var (dispatcher, session) = Readers();
        (EventBoardSubOpcode, EventBoardResult)? failed = null;
        session.Attendance.AttendanceFailedEvent += (sub, result) => failed = (sub, result);

        dispatcher.Dispatch(EventBoard(EventBoardSubOpcode.AttendanceClaim, (int)EventBoardWire.Channel, (int)EventBoardResult.InventoryFull));

        Assert.Equal((EventBoardSubOpcode.AttendanceClaim, EventBoardResult.InventoryFull), failed);
    }

    [Fact]
    public void AnItemRemoveFailureCarriesItsReason()
    {
        var (dispatcher, session) = Readers();
        (bool, ItemRemoveReason)? result = null;
        session.Items.ItemRemoveResultEvent += (ok, reason) => result = (ok, reason);

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_ITEM_REMOVE, 0, (byte)ItemRemoveReason.VipKeyLocked));

        Assert.Equal((false, ItemRemoveReason.VipKeyLocked), result);
    }

    [Fact]
    public void ABattleZoneCloseNamesTheClosedZone()
    {
        var (dispatcher, session) = Readers();
        (BattleEventNotice, int)? toggled = null;
        session.BattleEvent.BattleZoneToggleEvent += (notice, zone) => toggled = (notice, zone);

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_BATTLE_EVENT, (byte)BattleEventSubOpcode.Open, (byte)BattleEventNotice.ZoneClose, BattleZoneSeen));

        Assert.Equal((BattleEventNotice.ZoneClose, (int)BattleZoneSeen), toggled);
    }
}
