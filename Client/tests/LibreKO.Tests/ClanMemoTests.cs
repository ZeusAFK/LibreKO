using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ClanMemoTests
{
    [Theory]
    [InlineData(2, 30034)]
    [InlineData(3, 30030)]
    [InlineData(5, 6906)]
    [InlineData(0, 30028)]
    [InlineData(9, 30028)]
    public void FailuresUseTheRetailTexts(byte result, int text) => Assert.Equal(text, ClanMemo.FailureText(result));

    [Fact]
    public void AWrittenMemoComesBackWithItsText()
    {
        var reply = ClanMemoReply.Read(ServerReply(ClanMemo.Written, "Back at 9"));

        Assert.NotNull(reply);
        Assert.Equal(ClanMemo.Written, reply.Value.Result);
        Assert.Equal("Back at 9", reply.Value.Memo);
    }

    [Fact]
    public void ARefusalCarriesOnlyItsCode()
    {
        var reply = ClanMemoReply.Read(ServerReply(2, ""));

        Assert.NotNull(reply);
        Assert.Equal(2, reply.Value.Result);
        Assert.Equal("", reply.Value.Memo);
    }

    private static Packet ServerReply(byte result, string memo)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte((byte)KnightsSubOpcode.UpdateMemo);
        p.WriteByte(ClanMemo.WriteKind);
        p.WriteByte(result);
        p.WriteString(memo);
        p.ResetOffset();
        p.ReadByte();
        return p;
    }
}
