using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class DropListReplyTests
{
    private static Packet ServerDropList(int listed, ushort? total)
    {
        var p = new Packet(GameOpcodes.GS_TARGET_STATE);
        p.WriteInt(5001);
        p.WriteByte(1);
        p.WriteInt(700);
        p.WriteInt(1000);
        p.WriteInt(420);
        p.WriteByte((byte)listed);
        for (int i = 0; i < listed; i++)
        {
            p.WriteInt(389010000 + i);
            p.WriteByte((byte)DropBand.Common);
        }
        if (total is { } count) p.WriteUShort(count);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void ACutListCarriesTheFullCount()
    {
        var p = ServerDropList(listed: 64, total: 70);

        var reply = DropListReply.Read(p)!;

        Assert.Equal(64, reply.Items.Count);
        Assert.Equal(70, reply.Total);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void AListWithoutTheCountIsComplete()
    {
        var reply = DropListReply.Read(ServerDropList(listed: 3, total: null))!;

        Assert.Equal(3, reply.Total);
    }
}
