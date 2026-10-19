using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.Tests;

public class RankBoardTests : GameTestBase
{
    [Fact]
    public void AnEntryCarriesItsSlotNameAndClan()
    {
        var packet = RankBoardPacketWriter.Board(
        [
            new RankBoardEntry(RankBoardPacketWriter.FirstPlayerSlot, "Veteran", 12, 3, "Wolves", 2, 1),
            new RankBoardEntry(RankBoardPacketWriter.FirstPlayerSlot + 1, "Loner", RankBoardEntry.NoClan, 0, "", 0, 0),
        ]);

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_BATTLE_EVENT);
        packet.ResetOffset();
        packet.ReadByte().Should().Be((byte)BattleEventSubOpcode.RankBoard);
        packet.ReadByte().Should().Be(2);
        packet.ReadUShort().Should().Be(RankBoardPacketWriter.FirstPlayerSlot);
        packet.ReadString().Should().Be("Veteran");
        packet.ReadUShort().Should().Be(0);
        packet.ReadShort().Should().Be(12);
        packet.ReadShort().Should().Be(3);
        packet.ReadString().Should().Be("Wolves");
        packet.ReadByte().Should().Be(2);
        packet.ReadByte().Should().Be(1);
        packet.ReadUShort().Should().Be(RankBoardPacketWriter.FirstPlayerSlot + 1);
        packet.ReadString().Should().Be("Loner");
        packet.ReadUShort().Should().Be(0);
        packet.ReadShort().Should().Be(RankBoardEntry.NoClan);
    }

    [Fact]
    public async Task TheBoardListsTheNationsRankedClansAndMonthlyLeaders()
    {
        using var provider = CreateProvider(db =>
        {
            db.Accounts.AddRange(
                new Account { Id = 1, Login = "a", Password = "x", Nation = AccountNation.Karus },
                new Account { Id = 2, Login = "b", Password = "x", Nation = AccountNation.Karus },
                new Account { Id = 3, Login = "c", Password = "x", Nation = AccountNation.ElMorad });
            db.Characters.AddRange(
                new Character { Id = 7, AccountId = 1, Name = "Veteran", Race = 2, Class = 106, LoyaltyMonthly = 900, KnightsId = 12 },
                new Character { Id = 8, AccountId = 2, Name = "Rising", Race = 1, Class = 101, LoyaltyMonthly = 500 },
                new Character { Id = 9, AccountId = 3, Name = "Aurelia", Race = 12, Class = 201, LoyaltyMonthly = 950 });
        });
        var sessions = provider.GetRequiredService<SessionManager>();
        sessions.Knights.AddClan(12, new KnightsEntity { Id = 12, Name = "Wolves", Chief = "Veteran", Nation = (byte)AccountNation.Karus, Ranking = 1, Flag = 2, Grade = 3 });
        sessions.Knights.AddClan(13, new KnightsEntity { Id = 13, Name = "Doves", Chief = "Aurelia", Nation = (byte)AccountNation.ElMorad, Ranking = 1 });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var viewer = sessions.CreateSession(client, 8, 2);
        viewer.Nation = AccountNation.Karus;
        viewer.Name = "Rising";

        await provider.GetRequiredService<IRankBoardService>().SendAsync(viewer);

        var board = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_BATTLE_EVENT);
        board.ResetOffset();
        board.ReadByte().Should().Be((byte)BattleEventSubOpcode.RankBoard);
        var rows = Enumerable.Range(0, board.ReadByte()).Select(_ => ReadEntry(board)).ToList();
        rows.Should().Equal(
            (RankBoardPacketWriter.FirstClanSlot, "Veteran", "Wolves"),
            (RankBoardPacketWriter.FirstPlayerSlot, "Veteran", "Wolves"),
            ((ushort)(RankBoardPacketWriter.FirstPlayerSlot + 1), "Rising", ""));
    }

    private static (ushort, string, string) ReadEntry(Packet packet)
    {
        var slot = packet.ReadUShort();
        var name = packet.ReadString();
        packet.ReadUShort();
        packet.ReadShort();
        packet.ReadShort();
        var clan = packet.ReadString();
        packet.ReadByte();
        packet.ReadByte();
        return (slot, name, clan);
    }
}
