using LibreKO.Domain;
using LibreKO.Network;
using LibreKO.Network.Readers;
using LibreKO.Network.Requests;
using LibreKO.Session;
using Xunit;

namespace LibreKO.Tests;

public class DrakiTowerTests
{
    private const uint DefaultFinish = 3600;
    private const uint DefaultStage = 1;

    private static Packet RankList(byte myRank, uint entrancesLeft)
    {
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte((byte)TempleSubOpcode.DrakiList);
        p.WriteByte(1);
        p.WriteSByteString("Zeus");
        p.WriteUInt(895);
        p.WriteUInt(12);
        for (int i = 1; i < DrakiWire.TopRows; i++)
        {
            p.WriteByte((byte)(i + 1));
            p.WriteSByteString("");
            p.WriteUInt(DefaultFinish);
            p.WriteUInt(DefaultStage);
        }
        p.WriteByte(myRank);
        p.WriteSByteString("Rikka");
        p.WriteUInt(1200);
        p.WriteUInt(3);
        p.WriteUInt(7);
        p.WriteUInt(entrancesLeft);
        p.ResetOffset();
        return p;
    }

    private static (PacketDispatcher Dispatcher, GameSession Session) Readers()
    {
        var session = new GameSession();
        var dispatcher = new PacketDispatcher();
        PacketReaders.Register(dispatcher, session, new GameRequests(new NoSink(), session, new LoginState()), () => 0);
        return (dispatcher, session);
    }

    private sealed class NoSink : IPacketSink
    {
        public bool Connected => false;

        public void Send(Packet packet) { }
    }

    [Fact]
    public void TheRankListReadsTheTopRowsMyRowAndTheEntrances()
    {
        var (dispatcher, session) = Readers();
        DrakiRankList? list = null;
        session.Bifrost.DrakiRankListEvent += received => list = received;

        dispatcher.Dispatch(RankList(myRank: 40, entrancesLeft: 2));

        Assert.NotNull(list);
        Assert.Equal(DrakiWire.TopRows, list!.Top.Count);
        Assert.Equal(new DrakiRankRow(1, "Zeus", 895, 12), list.Top[0]);
        Assert.Equal(new DrakiRankRow(40, "Rikka", 1200, 3), list.Mine);
        Assert.Equal(7, list.MaxStage);
        Assert.Equal(2, list.EntrancesLeft);
    }

    [Fact]
    public void AStageEndFreezesTheTimerWithItsOutcome()
    {
        var (dispatcher, session) = Readers();
        var p = new Packet(GameOpcodes.GS_EVENT);
        p.WriteByte((byte)TempleSubOpcode.DrakiLeaveSecond);
        p.WriteByte(0x0C);
        p.WriteByte(0x04);
        p.WriteUShort(4);
        p.WriteUShort(2);
        p.WriteUInt(310);
        p.WriteByte(1);
        p.ResetOffset();

        dispatcher.Dispatch(p);

        Assert.Equal(new DrakiStageEnd(4, 2, 310, GaveUp: true), session.Bifrost.DrakiFinish);
        Assert.Equal(0UL, session.Bifrost.DrakiEndsAtMs);
    }

    [Fact]
    public void TheEnterRequestCarriesTheCertificateAndTheChapter()
    {
        var bytes = DrakiWire.Enter(DrakiTower.CertificateItem, 3).GetBytes();

        Assert.Equal((byte)GameOpcodes.GS_EVENT, bytes[0]);
        Assert.Equal((byte)TempleSubOpcode.DrakiEnter, bytes[1]);
        Assert.Equal(DrakiTower.CertificateItem, System.BitConverter.ToInt32(bytes, 2));
        Assert.Equal(3, bytes[6]);
    }

    [Theory]
    [InlineData(895, "14:55")]
    [InlineData(3600 + 65, "01:05")]
    [InlineData(0, "00:00")]
    public void FinishTimesShowMinutesAndSecondsWithinTheHour(int seconds, string expected) =>
        Assert.Equal(expected, DrakiTower.FinishTime(seconds));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void OnlyTheFirstHundredAreRanked(int rank, bool expected) =>
        Assert.Equal(expected, DrakiTower.Ranked(rank));

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 1, false)]
    [InlineData(3, 3, true)]
    [InlineData(4, 3, false)]
    [InlineData(5, 9, true)]
    [InlineData(6, 9, false)]
    [InlineData(0, 5, false)]
    public void OnlyTheSectorsUpToTheHighestReachedAreOpen(int sector, int maxStage, bool expected) =>
        Assert.Equal(expected, DrakiTower.SectorOpen(sector, maxStage));

    [Fact]
    public void TheBoardStaysOpenWhenTheServerAsksForIt() =>
        Assert.Null(DrakiTower.EnterText(DrakiEnterResult.ShowBoard));

    [Theory]
    [InlineData(DrakiEnterResult.NoLimit, 40069)]
    [InlineData(DrakiEnterResult.TowerFull, 40076)]
    [InlineData(DrakiEnterResult.LevelTooLow, 40077)]
    [InlineData(DrakiEnterResult.CannotEnterNow, 40077)]
    [InlineData(DrakiEnterResult.SilentRefusal, 40090)]
    public void EnterRefusalsUseTheirTexts(DrakiEnterResult result, int text) =>
        Assert.Equal(text, DrakiTower.EnterText(result)?.Id);
}
