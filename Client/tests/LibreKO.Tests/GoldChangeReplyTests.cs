using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class GoldChangeReplyTests
{
    [Fact]
    public void AnEarnedChangeCarriesTheNewBalance()
    {
        var p = new Packet(GameOpcodes.GS_GOLD_CHANGE);
        p.WriteByte((byte)GoldChangeKind.Earned);
        p.WriteInt(1_500);
        p.WriteInt(42_000);
        p.ResetOffset();

        var reply = GoldChangeReply.Read(p);

        Assert.NotNull(reply);
        Assert.True(reply.Value.ChangesBalance);
        Assert.Equal(1_500, reply.Value.Amount);
        Assert.Equal(42_000, reply.Value.Total);
    }

    [Fact]
    public void AJackpotReadsItsTimesAndUnitAndLeavesTheBalance()
    {
        var p = new Packet(GameOpcodes.GS_GOLD_CHANGE);
        p.WriteByte((byte)GoldChangeKind.Multiplied);
        p.WriteInt(0);
        p.WriteInt(0);
        p.WriteUShort(500);
        p.WriteInt(7_031);
        p.ResetOffset();

        var reply = GoldChangeReply.Read(p);

        Assert.NotNull(reply);
        Assert.False(reply.Value.ChangesBalance);
        Assert.Equal(500, reply.Value.Times);
        Assert.Equal(7_031, reply.Value.UnitId);
    }

    [Fact]
    public void AShortJackpotIsRefused()
    {
        var p = new Packet(GameOpcodes.GS_GOLD_CHANGE);
        p.WriteByte((byte)GoldChangeKind.Multiplied);
        p.WriteInt(0);
        p.WriteInt(0);
        p.ResetOffset();

        Assert.Null(GoldChangeReply.Read(p));
    }
}
