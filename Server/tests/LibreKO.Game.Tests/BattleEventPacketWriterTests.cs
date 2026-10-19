using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Xunit;

namespace LibreKO.Game.Tests;

public class BattleEventPacketWriterTests
{
    private const byte Zone = BattleZoneManager.ZONE_BATTLE2;

    private static Packet Read(Packet packet, BattleEventSubOpcode sub)
    {
        packet.ResetOffset();
        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_BATTLE_EVENT);
        packet.ReadByte().Should().Be((byte)sub);
        return packet;
    }

    [Fact]
    public void ZoneOpened_PutsTheOpenTypeAfterTheSubOpcode()
    {
        var packet = Read(BattleEventPacketWriter.ZoneOpened(BattleZoneManager.BATTLEZONE_OPEN, Zone), BattleEventSubOpcode.Open);

        packet.ReadByte().Should().Be(BattleZoneManager.BATTLEZONE_OPEN);
        packet.ReadByte().Should().Be(Zone);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ZoneClosed_NamesTheClosedZone()
    {
        var packet = Read(BattleEventPacketWriter.ZoneClosed(Zone), BattleEventSubOpcode.Open);

        packet.ReadByte().Should().Be(BattleZoneManager.BATTLEZONE_CLOSE);
        packet.ReadByte().Should().Be(Zone);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void Winner_DeclaresTheWinnerThenTheLoser()
    {
        var packets = BattleEventPacketWriter.Winner((byte)AccountNation.Karus);

        packets.Should().HaveCount(2);
        var winner = Read(packets[0], BattleEventSubOpcode.Result);
        winner.ReadByte().Should().Be(BattleZoneManager.DECLARE_WINNER);
        winner.ReadByte().Should().Be((byte)AccountNation.Karus);
        var loser = Read(packets[1], BattleEventSubOpcode.Result);
        loser.ReadByte().Should().Be(BattleZoneManager.DECLARE_LOSER);
        loser.ReadByte().Should().Be((byte)AccountNation.ElMorad);
    }
}
