using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class YutGameTests
{
    private sealed class RecordingSink : IPacketSink
    {
        public readonly List<byte[]> Sent = [];

        public bool Connected => true;

        public void Send(Packet packet) => Sent.Add(packet.GetBytes());
    }

    [Fact]
    public void TheRollReachesTheMiningSessionWithBothDice()
    {
        var session = new GameSession();
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new RecordingSink(), session, new LoginState()), () => 0);
        YutRoll? roll = null;
        session.Mining.YutRollEvent += received => roll = received;
        var p = new Packet(GameOpcodes.GS_MINING);
        p.WriteByte((byte)MiningSubOpcode.BettingGame);
        p.WriteUShort((ushort)YutResult.Won);
        p.WriteInt(6);
        p.WriteUShort(0);
        p.WriteByte(4);
        p.WriteByte(2);
        p.ResetOffset();

        dispatcher.Dispatch(p);

        Assert.Equal(new YutRoll(YutResult.Won, 6, 4, 2), roll);
    }

    [Fact]
    public void ABetSendsTheCoinKind()
    {
        var sink = new RecordingSink();
        new MiningRequests(sink).YutBet();

        Assert.Equal(new byte[] { (byte)GameOpcodes.GS_MINING, (byte)MiningSubOpcode.BettingGame, (byte)YutBetKind.Coins }, sink.Sent.Single());
    }

    [Theory]
    [InlineData(1, 40109, 40114)]
    [InlineData(5, 40113, 40118)]
    public void EachRollPlaysItsDie(int roll, int playerFx, int npcFx)
    {
        Assert.Equal(playerFx, YutGame.PlayerDieFx(roll));
        Assert.Equal(npcFx, YutGame.NpcDieFx(roll));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void AnUnknownRollPlaysNothing(int roll)
    {
        Assert.Null(YutGame.PlayerDieFx(roll));
        Assert.Null(YutGame.NpcDieFx(roll));
    }

    [Theory]
    [InlineData(YutResult.NotEnoughCoins, true)]
    [InlineData(YutResult.NoCoins, true)]
    [InlineData(YutResult.Lost, false)]
    public void OnlyTheCoinRefusalsStopTheGame(YutResult result, bool refused) =>
        Assert.Equal(refused, YutGame.Refused(result));

    [Theory]
    [InlineData(YutResult.Won, YutGame.WonFx, 16402)]
    [InlineData(YutResult.Tied, YutGame.TiedFx, 16403)]
    [InlineData(YutResult.Lost, YutGame.LostFx, 16404)]
    public void EachOutcomeHasItsEffectAndText(YutResult result, int fx, int text)
    {
        Assert.Equal(fx, YutGame.ResultFx(result));
        Assert.Equal(text, YutGame.ResultText(result)?.Id);
    }
}
