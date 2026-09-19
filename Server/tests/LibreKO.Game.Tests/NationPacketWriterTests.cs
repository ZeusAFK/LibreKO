using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class NationPacketWriterTests
{
    private const byte SiegeSub = 3;
    private const byte RankTypePkZone = 1;

    [Fact]
    public void CastleFlagIsTheSameWidthWithAndWithoutAMasterClan()
    {
        var owned = SiegePacketWriter.CastleFlag(
            SiegeSub, new SiegePacketWriter.ClanBanner(42, 7, 3, 1)).GetData().Length;
        var unowned = SiegePacketWriter.CastleFlag(SiegeSub, null).GetData().Length;

        owned.Should().Be(unowned);
    }

    [Fact]
    public void CastleFlagCarriesTheBannerFields()
    {
        var packet = SiegePacketWriter.CastleFlag(
            SiegeSub, new SiegePacketWriter.ClanBanner(42, 7, 3, 1));
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_SIEGE);
        packet.ReadByte().Should().Be(SiegeSub);
        packet.ReadByte().Should().Be(0);
        packet.ReadUShort().Should().Be(42);
        packet.ReadUShort().Should().Be(7);
        packet.ReadByte().Should().Be(3);
        packet.ReadByte().Should().Be(1);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void CastleScheduleCarriesDayHourMinute()
    {
        var packet = SiegePacketWriter.CastleSchedule(
            SiegeSub, 2, 1, 4, new SiegePacketWriter.WarSchedule(6, 20, 30));
        packet.ResetOffset();

        packet.ReadByte().Should().Be(SiegeSub);
        packet.ReadByte().Should().Be(2);
        packet.ReadUShort().Should().Be(1);
        packet.ReadUShort().Should().Be(4);
        packet.ReadByte().Should().Be(6);
        packet.ReadByte().Should().Be(20);
        packet.ReadByte().Should().Be(30);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void TariffChangedAlwaysReportsSuccess()
    {
        var packet = SiegePacketWriter.TariffChanged(SiegeSub, 4, 15, 21);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(SiegeSub);
        packet.ReadByte().Should().Be(4);
        packet.ReadUShort().Should().Be(1);
        packet.ReadUShort().Should().Be(15);
        packet.ReadByte().Should().Be(21);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PkZoneRankWritesBothNationsThenTheCallersOwnRank()
    {
        var karus = new[] { new RankPacketWriter.RankEntry("Aurelia", 1, 42, 7, "Wolves", 500) };
        var packet = RankPacketWriter.PkZone(RankTypePkZone, karus, [], 3, 500);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_RANK);
        packet.ReadByte().Should().Be(RankTypePkZone);
        packet.ReadUShort().Should().Be(1);
        packet.ReadSByteString().Should().Be("Aurelia");
        packet.ReadByte().Should().Be(1);
        packet.ReadUShort().Should().Be(42);
        packet.ReadUShort().Should().Be(7);
        packet.ReadSByteString().Should().Be("Wolves");
        packet.ReadInt().Should().Be(500);
        packet.ReadUShort().Should().Be(RankPacketWriter.NoPremiumBonus);
        packet.ReadByte().Should().Be(RankPacketWriter.NoSymbolRank);
        packet.ReadUShort().Should().Be(0);
        packet.ReadUShort().Should().Be(3);
        packet.ReadInt().Should().Be(500);
        packet.ReadUShort().Should().Be(RankPacketWriter.NoPremiumBonus);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void ChaosDungeonRankEndsWithItsTrailingConstant()
    {
        var packet = RankPacketWriter.ChaosDungeon(3);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadInt();

        packet.ReadInt().Should().Be(RankPacketWriter.ChaosDungeonTrailer);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void BifrostCarriesSecondsRemaining()
    {
        var packet = BifrostPacketWriter.Remaining(TempleSubOpcode.BifrostRemaining, 600);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_BIFROST);
        packet.ReadByte().Should().Be((byte)TempleSubOpcode.BifrostRemaining);
        packet.ReadInt().Should().Be(600);
        packet.ReadByte().Should().Be(0);
        packet.RemainingBytes.Should().Be(0);
    }
}
