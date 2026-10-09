using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class RebirthWireTests
{
    private static Packet Reply(params byte[] body)
    {
        var p = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        p.WriteBytes(body);
        p.ResetOffset();
        return p;
    }

    [Theory]
    [InlineData(new byte[] { Net.ClassChangeRebirthStat, 0x01, 0x00 }, RebirthWire.Accepted)]
    [InlineData(new byte[] { Net.ClassChangeRebirthStat, 0xFE, 0xFF }, -2)]
    [InlineData(new byte[] { Net.ClassChangeRebirthStat, 0xFD, 0xFF }, -3)]
    [InlineData(new byte[] { Net.ClassChangeRebirthStat, 0xFB, 0xFF }, -5)]
    [InlineData(new byte[] { Net.ClassChangeRebirthStat, 0xF9, 0xFF }, RebirthWire.Busy)]
    public void TheServerWriterBytesReadAsTheSignedResult(byte[] body, short expected)
    {
        var p = Reply(body);

        Assert.Equal(Net.ClassChangeRebirthStat, p.ReadByte());
        Assert.True(RebirthWire.TryRead(p, out short result));
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(RebirthWire.RefusedFirst)]
    [InlineData(RebirthWire.RefusedLast)]
    public void EveryRefusalCodeIsRead(short code)
    {
        var p = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        p.WriteShort(code);
        p.ResetOffset();

        Assert.True(RebirthWire.TryRead(p, out short result));
        Assert.Equal(code, result);
    }

    [Theory]
    [InlineData(new byte[] { 0x01 })]
    [InlineData(new byte[] { 0x00, 0x00 })]
    [InlineData(new byte[] { 0x02, 0x00 })]
    [InlineData(new byte[] { 0xF5, 0xFF })]
    [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 })]
    public void ALegacyOrUnknownReplyIsNotARebirthResult(byte[] tail)
    {
        Assert.False(RebirthWire.TryRead(Reply(tail), out _));
    }

    [Theory]
    [InlineData(RebirthWire.Accepted, RebirthWire.AcceptedText)]
    [InlineData(-1, 33000)]
    [InlineData(-2, 33100)]
    [InlineData(-3, 33101)]
    [InlineData(-4, 33102)]
    [InlineData(-5, 33103)]
    [InlineData(-6, 33104)]
    [InlineData(-7, RebirthWire.UnavailableText)]
    [InlineData(-8, RebirthWire.UnavailableText)]
    [InlineData(-9, RebirthWire.UnavailableText)]
    [InlineData(RebirthWire.RefusedLast, RebirthWire.UnavailableText)]
    public void EachResultShowsItsRetailText(short result, int text)
    {
        Assert.Equal(text, RebirthWire.ResultText(result));
    }
}
