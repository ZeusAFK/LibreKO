using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using Xunit;

namespace LibreKO.Game.Tests;

public class UserInfoPacketWriterTests
{
    private const int VisualSlots = 17;
    private const int VisualEntryBytes = 7;
    private const int ClanlessBlockBytes = 14;

    private static UserInfoPacketWriter.VisualItem[] Visuals =>
        Enumerable.Range(0, VisualSlots)
            .Select(i => new UserInfoPacketWriter.VisualItem(100_000 + i, (short)(i + 1), (byte)i))
            .ToArray();

    private static UserInfoPacketWriter.UserState Clanless => new(
        Name: "Aurelia", Nation: 1, KnightsId: 0, KnightsFame: 0, Clan: null,
        NoClanNationCode: 93, Level: 62, Race: 1, Class: 105,
        X: 5430, Z: 3770, Y: 120, Face: 2, Hair: 7, Pose: 1,
        NeedParty: false, IsGameMaster: false, IsPartyLeader: false, Invisibility: 0,
        Direction: 90, ZoneId: 21, IsHidingHelmet: false, DisplayTitleId: 0, Visuals: Visuals);

    private static Packet Record(UserInfoPacketWriter.UserState state)
    {
        var packet = new Packet(GameOpcodes.GS_USER_INOUT);
        UserInfoPacketWriter.WriteRecord(packet, state);
        return packet;
    }

    [Fact]
    public void InOutLeadsWithTypeThenAPadByteThenTheCharacterId()
    {
        var packet = UserInfoPacketWriter.InOut(1, 70_001, Clanless);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_USER_INOUT);
        packet.ReadByte().Should().Be(1);
        packet.ReadByte().Should().Be(0);
        packet.ReadInt().Should().Be(70_001);
        packet.ReadSByteString().Should().Be("Aurelia");
    }

    [Fact]
    public void DespawnCarriesNoRecord()
    {
        var packet = UserInfoPacketWriter.InOut(2, 70_001, null);
        packet.ResetOffset();

        packet.ReadByte().Should().Be(2);
        packet.ReadByte().Should().Be(0);
        packet.ReadInt().Should().Be(70_001);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void TheClanBranchAddsExactlyTheClanNameBytes()
    {
        var clanless = Record(Clanless).GetData().Length;
        var clanned = Record(Clanless with
        {
            KnightsId = 42,
            Clan = new UserInfoPacketWriter.ClanState(7, "Wolves", 3, 0, 12, 1, 2, 3, 4),
        }).GetData().Length;

        (clanned - clanless).Should().Be("Wolves".Length);
    }

    [Fact]
    public void TheRecordEndsWithSeventeenVisualSlotsThenTheZoneTail()
    {
        var data = Record(Clanless).GetData();
        var tailBytes = 2 + 4 + 1 + 4 + 1 + 1 + 1 + 1 + 2 + 4 + 1 + 1 + 1 + 2;
        var visualsStart = data.Length - tailBytes - (VisualSlots * VisualEntryBytes);

        var packet = Record(Clanless);
        packet.ResetOffset();
        packet.ReadBytes(visualsStart);

        for (var i = 0; i < VisualSlots; i++)
        {
            packet.ReadInt().Should().Be(100_000 + i);
            packet.ReadShort().Should().Be((short)(i + 1));
            packet.ReadByte().Should().Be((byte)i);
        }

        packet.ReadUShort().Should().Be(21);
        packet.ReadInt().Should().Be(-1);
    }

    [Fact]
    public void NameAndClanlessBlockSitAtTheirKnownOffsets()
    {
        var packet = Record(Clanless);
        packet.ResetOffset();

        packet.ReadSByteString().Should().Be("Aurelia");
        packet.ReadByte().Should().Be(1);
        packet.ReadBytes(3);
        packet.ReadShort().Should().Be(0);
        packet.ReadByte().Should().Be(0);

        packet.ReadInt().Should().Be(0);
        packet.ReadShort().Should().Be(0);
        packet.ReadByte().Should().Be(0);
        packet.ReadUShort().Should().Be(93);
        packet.ReadInt().Should().Be(0);
        packet.ReadByte().Should().Be(0);

        packet.ReadByte().Should().Be(62);
        packet.ReadByte().Should().Be(1);
        packet.ReadShort().Should().Be(105);
        packet.ReadShort().Should().Be(5430);
        packet.ReadShort().Should().Be(3770);
        packet.ReadShort().Should().Be(120);
    }

    [Fact]
    public void TheDisplayTitleIsTheLastFieldOfTheRecord()
    {
        var packet = Record(Clanless with { DisplayTitleId = 137 });
        var data = packet.GetData();

        packet.ResetOffset();
        packet.ReadBytes(data.Length - 2);
        packet.ReadUShort().Should().Be(137);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void GameMasterAuthorityIsZeroAndPlayersAreOne()
    {
        static byte AuthorityOf(UserInfoPacketWriter.UserState state)
        {
            var packet = Record(state);
            packet.ResetOffset();
            packet.ReadSByteString();
            packet.ReadBytes(1 + 3 + 2 + 1 + ClanlessBlockBytes + 1 + 1 + 2 + 2 + 2 + 2 + 1 + 4 + 1 + 4 + 1 + 1);
            return packet.ReadByte();
        }

        AuthorityOf(Clanless).Should().Be(UserInfoPacketWriter.AuthorityPlayer);
        AuthorityOf(Clanless with { IsGameMaster = true }).Should().Be(UserInfoPacketWriter.AuthorityGameMaster);
    }
}
