using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class UserInfoReplyTests
{
    private static Packet Reply(int party, int[] places, (int Type, short B, short S, short G)[] marks, (int Id, int Count)[] special)
    {
        var p = new Packet(GameOpcodes.GS_USER_INFO);
        p.WriteByte((byte)PlayerInspectResult.Accepted);
        p.WriteSByteString("Rikka");
        p.WriteByte(72);
        p.WriteShort(211);
        p.WriteInt(1_234_567);
        p.WriteInt(890);
        p.WriteByte(0);
        p.WriteShort(17);
        p.WriteShort(3);
        p.WriteByte(1);
        p.WriteByte(4);
        p.WriteSByteString("Vipers");
        p.WriteSByteString("Zeus");
        p.WriteByte(2);
        p.WriteByte(1);
        p.WriteShort((short)party);
        foreach (int place in places) p.WriteInt(place);
        p.WriteByte((byte)marks.Length);
        foreach (var m in marks)
        {
            p.WriteShort((short)m.Type);
            p.WriteShort(m.B);
            p.WriteShort(m.S);
            p.WriteShort(m.G);
        }
        p.WriteByte((byte)special.Length);
        foreach (var s in special)
        {
            p.WriteShort((short)s.Id);
            p.WriteShort((short)s.Count);
        }
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void TheServersLayoutReadsBack()
    {
        var (result, info) = UserInfoReply.Read(Reply(-1, new[] { 4, 255, 0, 9, 12, 0, 1 }, System.Array.Empty<(int, short, short, short)>(), System.Array.Empty<(int, int)>()));

        Assert.Equal((byte)PlayerInspectResult.Accepted, result);
        Assert.NotNull(info);
        Assert.Equal("Rikka", info!.Name);
        Assert.Equal(211, info.Class);
        Assert.Equal(1_234_567, info.Loyalty);
        Assert.Equal("Vipers", info.ClanName);
        Assert.Equal(2, info.ClanRanking);
        Assert.False(info.InParty);
        Assert.Equal(new[] { 4, 255, 0, 9, 12, 0, 1 }, info.RankPlaces);
        Assert.Empty(info.Marks);
    }

    [Fact]
    public void TheSecondMarkBlockHasTwoShortsPerEntry()
    {
        var (_, info) = UserInfoReply.Read(Reply(5, new int[7],
            new[] { (0, (short)2, (short)1, (short)0), (3, (short)9, (short)9, (short)9) },
            new[] { (1, 4), (2, 0) }));

        Assert.True(info!.InParty);
        Assert.Equal(new[] { new RankMark(0, 2, 1, 0), new RankMark(51, 0, 0, 4) }, info.Marks);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ARefusalCarriesNoCard(byte code)
    {
        var p = new Packet(GameOpcodes.GS_USER_INFO);
        p.WriteByte(code);
        p.ResetOffset();

        var (result, info) = UserInfoReply.Read(p);

        Assert.Equal(code, result);
        Assert.Null(info);
    }
}
