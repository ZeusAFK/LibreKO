using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class WarehouseMoveTests : GameTestBase
{
    private const byte MoveSub = 4;
    private const int ItemId = 156210000;
    private const int PageSize = 24;

    [Fact]
    public async Task AMoveWithoutADestinationPageStaysOnTheSourcePage()
    {
        var session = await Move(srcPage: 0, srcCell: 0, dstCell: 3, dstPage: null);
        session.Warehouse[3].ItemId.Should().Be(ItemId);
        session.Warehouse[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AMoveCanCrossToAnotherPage()
    {
        var session = await Move(srcPage: 0, srcCell: 0, dstCell: 3, dstPage: 1);
        session.Warehouse[PageSize + 3].ItemId.Should().Be(ItemId);
        session.Warehouse[0].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task AMoveOntoATakenCellIsRefused()
    {
        var session = await Move(srcPage: 0, srcCell: 0, dstCell: 1, dstPage: 0, occupyDestination: true);
        session.Warehouse[0].ItemId.Should().Be(ItemId);
    }

    private async Task<UserSession> Move(byte srcPage, byte srcCell, byte dstCell, byte? dstPage, bool occupyDestination = false)
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            gameData.GetItem(ItemId).Returns(new ItemData { Num = ItemId, Countable = 0, Duration = 50 }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 73, accountId: 83);
        int source = srcPage * PageSize + srcCell;
        session.Warehouse[source].ItemId = ItemId;
        session.Warehouse[source].Count = 1;
        session.Warehouse[source].Durability = 50;
        if (occupyDestination)
        {
            int taken = (dstPage ?? srcPage) * PageSize + dstCell;
            session.Warehouse[taken].ItemId = ItemId + 1;
            session.Warehouse[taken].Count = 1;
        }

        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte(MoveSub);
        packet.WriteInt(1000);
        packet.WriteInt(ItemId);
        packet.WriteByte(srcPage);
        packet.WriteByte(srcCell);
        packet.WriteByte(dstCell);
        if (dstPage is { } page) packet.WriteByte(page);
        await provider.GetRequiredService<IWarehousePacketCoordinator>().HandleAsync(client, packet);
        return session;
    }
}
