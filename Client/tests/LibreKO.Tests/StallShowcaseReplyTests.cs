using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class StallShowcaseReplyTests
{
    private const int Owner = 7301;
    private const byte Selling = 1;
    private const byte PremiumFlags = 1;

    private static Packet Head(byte flags)
    {
        var p = new Packet(GameOpcodes.GS_MERCHANT_INOUT);
        p.WriteInt(Owner);
        p.WriteByte(Selling);
        p.WriteByte(flags);
        return p;
    }

    [Fact]
    public void AnOrdinaryStallNamesTheEmptiedSlot()
    {
        var p = Head(0);
        p.WriteByte(2);
        p.ResetOffset();

        var reply = StallShowcaseReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(Owner, reply.Value.Owner);
        Assert.Equal(2, reply.Value.Slot);
        Assert.Null(reply.Value.ItemIds);
    }

    [Fact]
    public void APremiumStallSendsItsEightShowcaseItems()
    {
        var p = Head(PremiumFlags);
        for (int i = 0; i < MerchantWire.StallDisplaySlotsPremium; i++) p.WriteInt(i == 3 ? 0 : 389010000 + i);
        p.ResetOffset();

        var reply = StallShowcaseReply.Read(p);

        Assert.NotNull(reply);
        Assert.Equal(StallShowcaseReply.NoSlot, reply.Value.Slot);
        Assert.Equal(MerchantWire.StallDisplaySlotsPremium, reply.Value.ItemIds!.Length);
        Assert.Equal(0, reply.Value.ItemIds[3]);
        Assert.Equal(389010007, reply.Value.ItemIds[7]);
        Assert.Equal(0, p.RemainingBytes);
    }
}
