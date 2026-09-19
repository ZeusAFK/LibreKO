using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class PlayerInspectPacketWriterTests
{
    private static PlayerInspectPacketWriter.Detail SampleDetail() => new(
        "Tester12", 72, 105, 1_200, 340,
        new PlayerInspectPacketWriter.Clan(9, 3, 1, 2, "Vipers", "Chief", 2),
        1,
        [1, 2, 3, 4, 5, 6, 7]);

    private static PlayerInspectPacketWriter.Equipment SampleEquipment(
        IReadOnlyList<PlayerInspectPacketWriter.WornItem> worn) => new(
        "Tester12", 105, 6, 2, 7, 72, 1, 1,
        900, 400,
        60, 0, 61, 0, 62, 0, 63, 0, 64, 0,
        250, 310,
        1, 2, 3, 4, 5, 6,
        worn);

    [Fact]
    public void Detail_LeadsWithTheAcceptedMarkerTheClientDemands()
    {
        var packet = PlayerInspectPacketWriter.UserDetail(SampleDetail());
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_USER_INFO);
        packet.ReadByte().Should().Be((byte)PlayerInspectSubOpcode.Detail);
        packet.ReadByte().Should().Be(
            PlayerInspectPacketWriter.DetailAccepted,
            because: "the client ignores the whole packet unless this byte is 1");
    }

    [Fact]
    public void Detail_RefusalIsNotTheAcceptedMarker()
    {
        var packet = PlayerInspectPacketWriter.DetailRefused();
        packet.ResetOffset();
        packet.ReadByte();

        packet.ReadByte().Should().NotBe(PlayerInspectPacketWriter.DetailAccepted);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Detail_FieldOrderMatchesTheClientsReads()
    {
        var packet = PlayerInspectPacketWriter.UserDetail(SampleDetail());
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();

        packet.ReadSByteString().Should().Be("Tester12");
        packet.ReadByte().Should().Be(72);
        packet.ReadShort().Should().Be(105);
        packet.ReadInt().Should().Be(1_200);
        packet.ReadInt().Should().Be(340);
        packet.ReadByte();

        packet.ReadShort().Should().Be(9);
        packet.ReadShort().Should().Be(3);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(2);
        packet.ReadSByteString().Should().Be("Vipers");
        packet.ReadSByteString().Should().Be("Chief");
        packet.ReadByte().Should().Be(2);

        packet.ReadByte().Should().Be(1);
        packet.ReadShort().Should().Be(0);

        for (var i = 1; i <= PlayerInspectPacketWriter.VisibleEquipmentCount; i++)
            packet.ReadInt().Should().Be(i);

        packet.ReadByte().Should().Be(0);
        packet.ReadByte().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Equipment_LeadsWithTheAcceptedResult()
    {
        var packet = PlayerInspectPacketWriter.EquipmentView(SampleEquipment([]));
        packet.ResetOffset();
        packet.ReadByte().Should().Be((byte)PlayerInspectSubOpcode.Equipment);
        packet.ReadShort().Should().Be(
            (short)EquipmentViewResult.Accepted,
            because: "a non-zero leading short is a refusal the client renders instead");

        packet.ReadSByteString().Should().Be("Tester12");
    }

    [Fact]
    public void Equipment_AlwaysCarriesEverySlotEvenWhenEmpty()
    {
        var oneItem = PlayerInspectPacketWriter.EquipmentView(
            SampleEquipment([new PlayerInspectPacketWriter.WornItem(700, 5, 1, 0)]));
        var noItems = PlayerInspectPacketWriter.EquipmentView(SampleEquipment([]));

        oneItem.GetLength().Should().Be(
            noItems.GetLength(),
            because: "the client reads a fixed slot count, so a short list would run it off the end");
    }

    [Fact]
    public void Equipment_SlotCountIsFourteenWornPlusNineCospre()
    {
        PlayerInspectPacketWriter.EquipmentSlotCount.Should().Be(23);

        var packet = PlayerInspectPacketWriter.EquipmentView(SampleEquipment([]));
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadSByteString();

        const int headerAfterName = 2 + 1 + 1 + 4 + 1 + 1 + 1 + 2 + 2 + 2 + 10 + 2 + 2 + (6 * 2);
        const int perSlot = 4 + 2 + 1 + 4;

        packet.RemainingBytes.Should().Be(headerAfterName + (23 * perSlot) + 1);
    }

    [Fact]
    public void Equipment_WritesAttackBeforeDefence()
    {
        var packet = PlayerInspectPacketWriter.EquipmentView(SampleEquipment([]));
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadSByteString();
        packet.ReadShort();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadShort();
        packet.ReadShort();
        packet.ReadShort();
        for (var i = 0; i < 10; i++)
            packet.ReadByte();

        packet.ReadShort().Should().Be(250);
        packet.ReadShort().Should().Be(310);
    }
}
