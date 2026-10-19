using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class CapeWireTests
{
    private const short ClanId = 42, Reserved = 0, CapeId = 7;
    private const byte Red = 10, Green = 20, Blue = 30;
    private const byte UnknownOp = (int)CapeSubOpcode.TicketPurchase + 1;

    private static Packet Reply(params object[] parts)
    {
        var p = new Packet(GameOpcodes.GS_CAPE);
        foreach (var part in parts)
        {
            switch (part)
            {
                case byte b: p.WriteByte(b); break;
                case short s: p.WriteShort(s); break;
                case int i: p.WriteInt(i); break;
            }
        }
        p.ResetOffset();
        return p;
    }

    private static int Colour(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [Fact]
    public void AChangedReplyCarriesTheClanCapeAndDye()
    {
        Assert.True(CapeWire.TryRead(
            Reply((short)CapeResult.Changed, ClanId, Reserved, CapeId, Colour(Red, Green, Blue)), out var reply));

        Assert.True(reply.Changed);
        Assert.Equal((ClanId, CapeId), ((short)reply.ClanId, (short)reply.CapeId));
        Assert.Equal((Red, Green, Blue), ((byte)reply.R, (byte)reply.G, (byte)reply.B));
    }

    [Fact]
    public void ADyeOnlyReplyKeepsNoCape()
    {
        Assert.True(CapeWire.TryRead(
            Reply((short)CapeResult.Changed, ClanId, Reserved, (short)CapeWire.NoCape, Colour(Red, Green, Blue)), out var reply));

        Assert.Equal(CapeWire.NoCape, reply.CapeId);
    }

    [Fact]
    public void AChangedReplyNeedsTheExactTail()
    {
        Assert.False(CapeWire.TryRead(Reply((short)CapeResult.Changed, ClanId, Reserved, CapeId), out _));
        Assert.False(CapeWire.TryRead(
            Reply((short)CapeResult.Changed, ClanId, Reserved, CapeId, Colour(Red, Green, Blue), (byte)0), out _));
    }

    [Fact]
    public void AChangedReplyRejectsAColourOutsideRgb()
    {
        Assert.False(CapeWire.TryRead(
            Reply((short)CapeResult.Changed, ClanId, Reserved, CapeId, ~CapeWire.ColourMask), out _));
    }

    [Theory]
    [InlineData(CapeWire.RefusedFirst)]
    [InlineData(CapeWire.RefusedLast)]
    public void ARefusalIsABareKnownCode(CapeResult code)
    {
        Assert.True(CapeWire.TryRead(Reply((short)code), out var reply));
        Assert.False(reply.Changed);
        Assert.Equal(code, reply.Result);

        Assert.False(CapeWire.TryRead(Reply((short)code, ClanId), out _));
    }

    [Theory]
    [InlineData((short)(CapeWire.RefusedFirst + 1))]
    [InlineData((short)(CapeWire.RefusedLast - 1))]
    public void AnUnknownCodeIsNotAReply(short code)
    {
        Assert.False(CapeWire.TryRead(Reply(code), out _));
    }

    [Fact]
    public void AnEmptyPacketIsNotAReply()
    {
        Assert.False(CapeWire.TryRead(Reply(), out _));
    }

    [Fact]
    public void OnlyKnownOpsAndWireSizedCapesAreRequested()
    {
        Assert.True(CapeWire.IsRequest(CapeSubOpcode.NormalPurchase, CapeId));
        Assert.True(CapeWire.IsRequest(CapeSubOpcode.TicketPurchase, CapeWire.NoCape));
        Assert.False(CapeWire.IsRequest((CapeSubOpcode)UnknownOp, CapeId));
        Assert.False(CapeWire.IsRequest(CapeSubOpcode.NormalPurchase, CapeWire.NoCape - 1));
        Assert.False(CapeWire.IsRequest(CapeSubOpcode.NormalPurchase, short.MaxValue + 1));
    }
}
