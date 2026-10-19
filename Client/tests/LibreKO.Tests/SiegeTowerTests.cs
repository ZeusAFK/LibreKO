using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class SiegeTowerTests
{
    private static Packet Bytes(params object[] parts)
    {
        var p = new Packet(GameOpcodes.GS_SIEGE_TOWER);
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte b: p.WriteByte(b); break;
                case ushort u: p.WriteUShort(u); break;
                case int i: p.WriteInt(i); break;
            }
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheUseReplyCarriesTheUserTheTowerAndItsPositionInTenths()
    {
        var reply = SiegeTowerReply.Read(Bytes((byte)SiegeTowerSubOpcode.Use, (byte)1, 7, 10042, (ushort)5005, (ushort)7801, (ushort)790));

        Assert.Equal(new SiegeTowerReply(SiegeTowerSubOpcode.Use, SiegeTowerResult.Succeeded, 7, 10042, 500.5f, 780.1f), reply);
    }

    [Fact]
    public void TheExitReplyCarriesTheUserAndTheTower()
    {
        var reply = SiegeTowerReply.Read(Bytes((byte)SiegeTowerSubOpcode.Exit, (byte)1, 7, 10042));

        Assert.Equal(new SiegeTowerReply(SiegeTowerSubOpcode.Exit, SiegeTowerResult.Succeeded, 7, 10042, 0f, 0f), reply);
    }

    [Fact]
    public void ABoardReplyIgnoresItsThreeWordsAndARefusalIsJustTheCode()
    {
        Assert.Equal(new SiegeTowerReply(SiegeTowerSubOpcode.Board, SiegeTowerResult.Succeeded, 0, 0, 0f, 0f),
            SiegeTowerReply.Read(Bytes((byte)SiegeTowerSubOpcode.Board, (byte)1, (ushort)5000, (ushort)7800, (ushort)790)));
        Assert.Equal(new SiegeTowerReply(SiegeTowerSubOpcode.Use, (SiegeTowerResult)8, 0, 0, 0f, 0f),
            SiegeTowerReply.Read(Bytes((byte)SiegeTowerSubOpcode.Use, (byte)8)));
        Assert.Null(SiegeTowerReply.Read(Bytes((byte)SiegeTowerSubOpcode.Use)));
    }

    [Theory]
    [InlineData(0, 16801)]
    [InlineData(2, 16802)]
    [InlineData(3, 16803)]
    [InlineData(4, 16804)]
    [InlineData(5, 16800)]
    [InlineData(6, 16805)]
    public void BoardRefusalsUseThe2619Texts(int result, int text) => Assert.Equal(text, SiegeTower.BoardRefusal(result).Id);

    [Theory]
    [InlineData(0, 16809)]
    [InlineData(2, 16807)]
    [InlineData(3, 16808)]
    [InlineData(4, 16806)]
    [InlineData(5, 16808)]
    public void LeaveRefusalsUseThe2619Texts(int result, int text) => Assert.Equal(text, SiegeTower.LeaveRefusal(result).Id);

    [Fact]
    public void UseAndEndTextsFollowTheirCodes()
    {
        Assert.Equal(16821, SiegeTower.UseRefusal(8).Id);
        Assert.Equal(16818, SiegeTower.UseRefusal(2).Id);
        Assert.Equal(16811, SiegeTower.EndedText(SiegeTowerResult.Failed).Id);
        Assert.Equal(16812, SiegeTower.EndedText(SiegeTowerResult.Succeeded).Id);
    }

    [Fact]
    public void InTheSiegeZoneOnlyTheClanOrAllianceBoardsElsewhereAnyoneNotHostile()
    {
        Assert.True(SiegeTower.MayBoard(true, 40, 0, 40, 0, true));
        Assert.True(SiegeTower.MayBoard(true, 40, 300, 50, 300, true));
        Assert.False(SiegeTower.MayBoard(true, 40, 0, 50, 0, false));
        Assert.True(SiegeTower.MayBoard(false, 40, 0, 50, 0, false));
        Assert.False(SiegeTower.MayBoard(false, 40, 0, 40, 0, true));
    }

    [Fact]
    public void TheCooldownLineRoundsUpAndSiegeLooksAreKnown()
    {
        Assert.Equal(10, SiegeTower.CooldownSeconds(9.2));
        Assert.Equal(1, SiegeTower.CooldownSeconds(0.1));
        Assert.True(SiegeTower.InSiegeLook(SiegeTower.GuardTowerLook));
        Assert.False(SiegeTower.InSiegeLook(30020));
    }
}
