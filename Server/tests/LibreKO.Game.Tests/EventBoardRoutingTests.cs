using System.Linq;
using FluentAssertions;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class EventBoardRoutingTests
{
    private static Packet Request(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
        packet.WriteByte(sub);
        packet.WriteUInt(0);
        packet.ResetOffset();
        return packet;
    }

    private static (EventBoardPacketCoordinator Board,
                    IAttendancePacketCoordinator Attendance,
                    IRoulettePacketCoordinator Roulette,
                    IClient Client) Create()
    {
        var attendance = Substitute.For<IAttendancePacketCoordinator>();
        var roulette = Substitute.For<IRoulettePacketCoordinator>();
        var client = Substitute.For<IClient>();
        var board = new EventBoardPacketCoordinator(
            attendance, roulette, NullLogger<EventBoardPacketCoordinator>.Instance);
        return (board, attendance, roulette, client);
    }

    [Theory]
    [InlineData((byte)EventBoardSubOpcode.AttendanceBoard)]
    [InlineData((byte)EventBoardSubOpcode.AttendanceClaim)]
    public async Task EventBoard_RoutesEventBoardSubOpcodesToAttendance(byte sub)
    {
        var (board, attendance, roulette, client) = Create();

        await board.HandleAsync(client, Request(sub));

        await attendance.Received(1).HandleAsync(client, Arg.Any<Packet>());
        await roulette.DidNotReceive().HandleAsync(Arg.Any<IClient>(), Arg.Any<Packet>());
    }

    [Theory]
    [InlineData((byte)EventBoardSubOpcode.RouletteOpen)]
    [InlineData((byte)EventBoardSubOpcode.RouletteAck)]
    [InlineData((byte)EventBoardSubOpcode.RouletteSpin)]
    [InlineData((byte)EventBoardSubOpcode.RoulettePrizeList)]
    public async Task EventBoard_RoutesEventBoardSubOpcodesToRoulette(byte sub)
    {
        var (board, attendance, roulette, client) = Create();

        await board.HandleAsync(client, Request(sub));

        await roulette.Received(1).HandleAsync(client, Arg.Any<Packet>());
        await attendance.DidNotReceive().HandleAsync(Arg.Any<IClient>(), Arg.Any<Packet>());
    }

    [Fact]
    public async Task EventBoard_RewindsSoTheDelegateStillReadsTheSubOpcode()
    {
        var (board, _, roulette, client) = Create();
        Packet? forwarded = null;
        await roulette.HandleAsync(Arg.Any<IClient>(), Arg.Do<Packet>(p => forwarded = p));

        await board.HandleAsync(client, Request((byte)EventBoardSubOpcode.RouletteSpin));

        forwarded.Should().NotBeNull();
        forwarded!.ReadByte().Should().Be((byte)EventBoardSubOpcode.RouletteSpin);
    }

    [Fact]
    public async Task EventBoard_IgnoresUnknownSubOpcode()
    {
        var (board, attendance, roulette, client) = Create();

        await board.HandleAsync(client, Request(0x7F));

        await attendance.DidNotReceive().HandleAsync(Arg.Any<IClient>(), Arg.Any<Packet>());
        await roulette.DidNotReceive().HandleAsync(Arg.Any<IClient>(), Arg.Any<Packet>());
    }

    [Fact]
    public void PrizeList_WritesRetailShape()
    {
        var entries = new[]
        {
            new RoulettePacketWriter.PrizeLogEntry(379_022_000, 3, 1_700_000_000),
            new RoulettePacketWriter.PrizeLogEntry(0, 5_000, 1_700_000_500),
        };

        var packet = RoulettePacketWriter.PrizeList(1, entries);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_EVENT_BOARD);
        packet.ReadByte().Should().Be((byte)EventBoardSubOpcode.RoulettePrizeList);
        packet.ReadInt().Should().Be(1);
        packet.ReadInt().Should().Be(RoulettePacketWriter.ResultOk);
        packet.ReadInt().Should().Be(2);

        foreach (var expected in entries)
        {
            packet.ReadInt().Should().Be(expected.ItemId);
            packet.ReadInt().Should().Be(expected.Quantity);
            packet.ReadInt().Should().Be(expected.UnixTime);
        }

        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void PrizeList_CapsAtTwentyEntriesTheClientCanRead()
    {
        var many = Enumerable.Range(1, 50)
            .Select(i => new RoulettePacketWriter.PrizeLogEntry(i, i, i))
            .ToArray();

        var packet = RoulettePacketWriter.PrizeList(1, many);
        packet.ResetOffset();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadInt();

        packet.ReadInt().Should().Be(RoulettePacketWriter.PrizeListMaxEntries);
        packet.RemainingBytes.Should().Be(RoulettePacketWriter.PrizeListMaxEntries * 12);
    }

    [Fact]
    public async Task EventBoard_IgnoresEmptyPayload()
    {
        var (board, attendance, roulette, client) = Create();
        var empty = new Packet(GameOpcodes.GS_EVENT_BOARD);

        await board.HandleAsync(client, empty);

        await attendance.DidNotReceive().HandleAsync(Arg.Any<IClient>(), Arg.Any<Packet>());
        await roulette.DidNotReceive().HandleAsync(Arg.Any<IClient>(), Arg.Any<Packet>());
    }
}
