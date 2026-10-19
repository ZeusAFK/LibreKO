using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class PacketDispatcherTests
{
    private enum TestSub : byte { First = 1, Second = 2, Third = 3 }

    private static Packet Incoming(GameOpcodes opcode, params byte[] body)
    {
        var p = new Packet(opcode);
        foreach (byte b in body) p.WriteByte(b);
        p.ResetOffset();
        return p;
    }

    [Fact]
    public void AnOpcodeReaderGetsTheWholePacket()
    {
        var dispatcher = new PacketDispatcher();
        int seen = -1;
        dispatcher.On(GameOpcodes.GS_MAIL, p => seen = p.ReadByte());

        Assert.True(dispatcher.Dispatch(Incoming(GameOpcodes.GS_MAIL, 7)));
        Assert.Equal(7, seen);
    }

    [Fact]
    public void AnOpcodeWithoutAReaderIsNotHandled()
    {
        var dispatcher = new PacketDispatcher();
        dispatcher.On(GameOpcodes.GS_MAIL, _ => { });

        Assert.False(dispatcher.Dispatch(Incoming(GameOpcodes.GS_CHAT, 1)));
    }

    [Fact]
    public void AnOpcodeCannotHaveTwoReaders()
    {
        var dispatcher = new PacketDispatcher();
        dispatcher.On(GameOpcodes.GS_MAIL, _ => { });

        Assert.Throws<InvalidOperationException>(() => dispatcher.On(GameOpcodes.GS_MAIL, _ => { }));
        Assert.Throws<InvalidOperationException>(() => dispatcher.On(GameOpcodes.GS_MAIL, TestSub.First, _ => { }));
    }

    [Fact]
    public void ASubOpcodeReaderStartsAfterTheSubOpcode()
    {
        var dispatcher = new PacketDispatcher();
        int seen = -1;
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, TestSub.Second, p => seen = p.ReadByte());

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_KNIGHTS_PROCESS, (byte)TestSub.Second, 9));

        Assert.Equal(9, seen);
    }

    [Fact]
    public void TheOtherSubOpcodesGoToTheOwnerWithTheirValue()
    {
        var dispatcher = new PacketDispatcher();
        var owned = new List<(TestSub, int)>();
        bool moved = false;
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, TestSub.Second, _ => moved = true);
        dispatcher.OnOtherSubs<TestSub>(GameOpcodes.GS_KNIGHTS_PROCESS, (p, sub) => owned.Add((sub, p.ReadByte())));

        dispatcher.Dispatch(Incoming(GameOpcodes.GS_KNIGHTS_PROCESS, (byte)TestSub.First, 4));
        dispatcher.Dispatch(Incoming(GameOpcodes.GS_KNIGHTS_PROCESS, (byte)TestSub.Third, 5));

        Assert.False(moved);
        Assert.Equal([(TestSub.First, 4), (TestSub.Third, 5)], owned);
    }

    [Fact]
    public void ASubOpcodeCannotHaveTwoReaders()
    {
        var dispatcher = new PacketDispatcher();
        dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, TestSub.First, _ => { });

        Assert.Throws<InvalidOperationException>(() => dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, TestSub.First, _ => { }));
        Assert.Throws<InvalidOperationException>(() => dispatcher.On(GameOpcodes.GS_KNIGHTS_PROCESS, _ => { }));
    }

    [Fact]
    public void AnEmptySubOpcodePacketIsSwallowed()
    {
        var dispatcher = new PacketDispatcher();
        bool called = false;
        dispatcher.OnOtherSubs<TestSub>(GameOpcodes.GS_KNIGHTS_PROCESS, (_, _) => called = true);

        Assert.True(dispatcher.Dispatch(Incoming(GameOpcodes.GS_KNIGHTS_PROCESS)));
        Assert.False(called);
    }
}
