using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ControlStateReplyTests
{
    [Fact]
    public void TheControlStateCarriesFlagsSpeedsAndTimedEffects()
    {
        var p = new Packet(GameOpcodes.GS_TARGET_STATE);
        p.WriteInt(901);
        p.WriteByte(ControlFlags.Stunned | ControlFlags.Slowed);
        p.WriteByte(40);
        p.WriteShort(100);
        p.WriteByte(2);
        p.WriteInt(110520);
        p.WriteByte(47);
        p.WriteInt(1800);
        p.WriteInt(109756);
        p.WriteByte(6);
        p.WriteInt(9500);
        p.ResetOffset();

        var reply = ControlStateReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(901, reply.Value.UnitId);
        Assert.Equal(ControlFlags.Stunned | ControlFlags.Slowed, reply.Value.Flags);
        Assert.Equal(40, reply.Value.MovePercent);
        Assert.Equal(2, reply.Value.Effects.Count);
        Assert.Equal(9500, reply.Value.LongestMs);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AClearedStateHasNoEffects()
    {
        var p = new Packet(GameOpcodes.GS_TARGET_STATE);
        p.WriteInt(901);
        p.WriteByte(0);
        p.WriteByte(100);
        p.WriteShort(100);
        p.WriteByte(0);
        p.ResetOffset();

        var reply = ControlStateReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(0, reply.Value.Flags);
        Assert.Empty(reply.Value.Effects);
        Assert.Equal(0, reply.Value.LongestMs);
    }
}
