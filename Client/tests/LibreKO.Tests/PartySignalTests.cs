using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PartySignalTests
{
    private const string Probe = "4178563412";
    private const uint ProbeToken = 0x12345678;
    private const string TwoPings = "420271110100540072110100FFFF";
    private const string NoPings = "4200";
    private const string Progress = "43711101007102";
    private const int Member = 70001;
    private const int SecondMember = 70002;

    private static Packet FromServer(string hex)
    {
        var p = new Packet(GameOpcodes.GS_PARTY);
        p.WriteBytes(Convert.FromHexString(hex));
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheProbeReadsAsItsToken()
    {
        var p = FromServer(Probe);

        Assert.Equal((byte)PartySubOpcode.SignalProbe, p.ReadByte());
        var probe = PartySignalProbe.Read(p);

        Assert.NotNull(probe);
        Assert.Equal(ProbeToken, probe.Value.Token);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void TheEchoRepeatsTheProbeBytes()
    {
        var echo = new PartySignalProbe(ProbeToken).Echo();

        Assert.Equal((byte)GameOpcodes.GS_PARTY, echo.GetOpcode());
        Assert.Equal(Convert.FromHexString(Probe), echo.GetData());
    }

    [Fact]
    public void AShortProbeIsRejected()
    {
        var p = FromServer(Probe[..^2]);
        p.ReadByte();

        Assert.Null(PartySignalProbe.Read(p));
    }

    [Fact]
    public void TheTableReadsEachMemberAndTheirRoundTrip()
    {
        var p = FromServer(TwoPings);

        Assert.Equal((byte)PartySubOpcode.SignalTable, p.ReadByte());
        var table = PartySignalTable.Read(p);

        Assert.NotNull(table);
        Assert.Equal(new[] { new PartySignalEntry(Member, 84), new PartySignalEntry(SecondMember, PartySignal.NoSignal) }, table);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AnEmptyTableReadsAsNoEntries()
    {
        var p = FromServer(NoPings);
        p.ReadByte();

        var table = PartySignalTable.Read(p);

        Assert.NotNull(table);
        Assert.Empty(table);
    }

    [Fact]
    public void ATableShorterThanItsCountIsRejected()
    {
        var p = FromServer(TwoPings[..^2]);
        p.ReadByte();

        Assert.Null(PartySignalTable.Read(p));
    }

    [Fact]
    public void LevelProgressReadsAsTheMemberAndTenthsOfAPercent()
    {
        var p = FromServer(Progress);

        Assert.Equal((byte)PartySubOpcode.LevelProgress, p.ReadByte());
        var progress = PartyLevelProgress.Read(p);

        Assert.NotNull(progress);
        Assert.Equal(new PartyLevelProgress(Member, 625), progress.Value);
        Assert.Equal(62.5, PartyHud.LevelPercent(progress.Value.Permille), 3);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void ShortLevelProgressIsRejected()
    {
        var p = FromServer(Progress[..^2]);
        p.ReadByte();

        Assert.Null(PartyLevelProgress.Read(p));
    }

    [Theory]
    [InlineData(-1, PartySignalLevel.Unknown, 0)]
    [InlineData(0, PartySignalLevel.Strong, 3)]
    [InlineData(100, PartySignalLevel.Strong, 3)]
    [InlineData(101, PartySignalLevel.Good, 2)]
    [InlineData(200, PartySignalLevel.Good, 2)]
    [InlineData(201, PartySignalLevel.Weak, 1)]
    [InlineData(350, PartySignalLevel.Weak, 1)]
    [InlineData(351, PartySignalLevel.Poor, 0)]
    [InlineData(PartySignal.NoSignal, PartySignalLevel.Lost, 0)]
    public void TheRoundTripPicksTheIcon(int ms, PartySignalLevel level, int arcs)
    {
        Assert.Equal(level, PartySignal.Level(ms));
        Assert.Equal(arcs, PartySignal.Arcs(PartySignal.Level(ms)));
    }

    [Fact]
    public void TheTooltipShowsTheRoundTripOrNoSignal()
    {
        Assert.Equal("Ping: 84 ms", PartySignal.Tooltip(84));
        Assert.Equal("No signal", PartySignal.Tooltip(PartySignal.NoSignal));
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(1000, 100.0)]
    [InlineData(1200, 100.0)]
    public void LevelProgressStaysWithinTheGauge(int permille, double percent) =>
        Assert.Equal(percent, PartyHud.LevelPercent(permille), 3);
}
