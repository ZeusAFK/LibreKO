using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class SiegeWarTests
{
    private const int OwnerClan = 120;
    private const int OwnerAlliance = 300;

    private static readonly SiegeWarStatus Held =
        new(SiegeWarState.Held, OwnerClan, 4, OwnerAlliance, 900, 2400, "Wolves");

    private static Packet StatusBytes(byte state, short clan, short mark, short alliance, int hold, short war, string name)
    {
        var p = new Packet(GameOpcodes.GS_SIEGE);
        p.WriteByte(state);
        p.WriteShort(clan);
        p.WriteShort(mark);
        p.WriteShort(alliance);
        p.WriteInt(hold);
        p.WriteShort(war);
        p.WriteSByteString(name);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheStatusIsReadInTheServerOrder()
    {
        var status = SiegeWarStatus.Read(StatusBytes(2, OwnerClan, 4, OwnerAlliance, 900, 2400, "Wolves"));

        Assert.Equal(Held, status);
        Assert.True(status!.Value.Running);
        Assert.True(status.Value.Held);
    }

    [Fact]
    public void AnUnheldWarRunsWithoutAnOwnerAndTheEndStopsIt()
    {
        var unheld = SiegeWarStatus.Read(StatusBytes(1, 0, 0, 0, 0, 3000, ""))!.Value;
        var ended = SiegeWarStatus.Read(StatusBytes(4, OwnerClan, 4, 0, 0, 0, "Wolves"))!.Value;

        Assert.True(unheld.Running);
        Assert.False(unheld.Held);
        Assert.False(ended.Running);
        Assert.False(SiegeWar.At(SiegeWar.Delos, ended));
        Assert.True(SiegeWar.At(SiegeWar.Delos, unheld));
        Assert.False(SiegeWar.At(SiegeWar.Delos + 1, unheld));
    }

    [Fact]
    public void AShortStatusIsIgnored()
    {
        var p = new Packet(GameOpcodes.GS_SIEGE);
        p.WriteByte(2);
        p.WriteShort(OwnerClan);
        p.ResetOffset();

        Assert.Null(SiegeWarStatus.Read(p));
    }

    [Fact]
    public void OnlyTheOwnerSideFightsEveryoneElse()
    {
        Assert.True(SiegeWar.Hostile(OwnerClan, 0, 77, 0, Held));
        Assert.True(SiegeWar.Hostile(77, 0, OwnerClan, 0, Held));
        Assert.True(SiegeWar.Hostile(55, OwnerClan, 77, 0, Held));
        Assert.False(SiegeWar.Hostile(77, 0, 88, 0, Held));
        Assert.False(SiegeWar.Hostile(OwnerClan, 0, 55, OwnerClan, Held));
    }

    [Fact]
    public void WithNoOwnerNobodyIsHostile()
    {
        var unheld = new SiegeWarStatus(SiegeWarState.Unheld, 0, 0, 0, 0, 3000, "");

        Assert.False(SiegeWar.Hostile(77, 0, 88, 0, unheld));
    }

    [Fact]
    public void ThePlateSideAcceptsTheOwnerClanOrItsAlliance()
    {
        Assert.True(SiegeWar.OwnerSide(OwnerClan, 0, Held));
        Assert.True(SiegeWar.OwnerSide(OwnerAlliance, 0, Held));
        Assert.True(SiegeWar.OwnerSide(55, OwnerAlliance, Held));
        Assert.False(SiegeWar.OwnerSide(55, 0, Held));
    }

    [Fact]
    public void TheTargetNameIsMaskedUnlessSharingClanOrAllianceOrBeingGm()
    {
        Assert.Equal(SiegeWar.DefenseMemberText, SiegeWar.TargetMaskText(false, 77, 0, OwnerClan, 0, Held));
        Assert.Equal(SiegeWar.DefenseMemberText, SiegeWar.TargetMaskText(false, 77, 0, 55, OwnerClan, Held));
        Assert.Equal(SiegeWar.SiegeMemberText, SiegeWar.TargetMaskText(false, OwnerClan, 0, 77, 0, Held));
        Assert.Equal(0, SiegeWar.TargetMaskText(false, 77, 0, 77, 0, Held));
        Assert.Equal(0, SiegeWar.TargetMaskText(false, 77, 300, 88, 300, Held));
        Assert.Equal(0, SiegeWar.TargetMaskText(true, 77, 0, 88, 0, Held));
    }

    [Fact]
    public void TheClockDropsHoursAndLeavesSecondsUnpadded()
    {
        const string template = "Castle Siege Time : [%2d : %d]";

        Assert.Equal("Castle Siege Time : [50 : 0]", SiegeWar.Clock(template, 3000));
        Assert.Equal("Castle Siege Time : [ 0 : 0]", SiegeWar.Clock(template, 3600));
        Assert.Equal("Castle Siege Time : [ 1 : 5]", SiegeWar.Clock(template, 65));
        Assert.Equal("Castle Siege Time : [ 0 : 0]", SiegeWar.Clock(template, -4));
    }

    [Fact]
    public void TheScrambleKeepsTheLengthInGlyphPairsAndATail()
    {
        Assert.Equal("★★", SiegeWar.Scramble("abcd", _ => 0));
        Assert.Equal("▒▒+", SiegeWar.Scramble("abcde", count => count - 1));
        Assert.Equal("!", SiegeWar.Scramble("a", _ => 0));
        Assert.Equal("", SiegeWar.Scramble("", _ => 0));
    }
}
