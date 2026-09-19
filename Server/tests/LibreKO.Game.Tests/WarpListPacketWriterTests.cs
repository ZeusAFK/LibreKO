using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class WarpListPacketWriterTests
{
    [Fact]
    public void ResultUsesSubTwoNotAnInventedSubZero()
    {
        var packet = WarpListPacketWriter.Result(WarpListPacketWriter.ResultNotQualified);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_WARP_LIST);
        packet.ReadByte().Should().Be((byte)WarpListSubOpcode.Result);
        packet.ReadByte().Should().Be(WarpListPacketWriter.ResultNotQualified);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ArrivedIsResultOne()
    {
        var packet = WarpListPacketWriter.Arrived();
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)WarpListSubOpcode.Result);
        packet.ReadByte().Should().Be(WarpListPacketWriter.ResultArrived);
    }

    [Fact]
    public void MenuIsACountedListOfEntries()
    {
        var entries = new[]
        {
            new WarpListPacketWriter.Entry(4, "Moradon", "Neutral city", 21, 0, 5_000),
        };

        var packet = WarpListPacketWriter.Menu(entries);
        packet.ResetOffset();

        packet.ReadByte().Should().Be((byte)WarpListSubOpcode.Menu);
        packet.ReadShort().Should().Be(1);
        packet.ReadShort().Should().Be(4);
        packet.ReadString().Should().Be("Moradon");
        packet.ReadString().Should().Be("Neutral city");
        packet.ReadShort().Should().Be(21);
        packet.ReadShort().Should().Be(0);
        packet.ReadUInt().Should().Be(5_000);
        packet.RemainingBytes.Should().Be(0);
    }
}
