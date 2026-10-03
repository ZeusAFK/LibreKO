using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class WarehouseStorageTests : GameTestBase
{
    private const byte InputSub = 2;
    private const byte Succeeded = 1;
    private const byte NonStorableRace = 73;
    private const byte VaultTicketRace = 77;

    [Fact]
    public async Task AnOrdinaryItemIsStored()
    {
        var (session, reply) = await Store(156210000, race: 0);
        reply.Should().Be(Succeeded);
        session.Warehouse[0].ItemId.Should().Be(156210000);
    }

    [Theory]
    [InlineData(810433000, NonStorableRace)]
    [InlineData(1113331000, VaultTicketRace)]
    [InlineData(910012000, 0)]
    public async Task ItemsTheRetailWarehouseRefusesStayInTheBag(int itemId, byte race)
    {
        var (session, reply) = await Store(itemId, race);
        reply.Should().NotBe(Succeeded);
        session.Warehouse[0].IsEmpty.Should().BeTrue();
        session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(itemId);
    }

    private async Task<(UserSession Session, byte Reply)> Store(int itemId, byte race)
    {
        using var provider = CreateProvider(_ => { }, gameData =>
            gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Race = race, Countable = 0, Duration = 50 }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 72, accountId: 82);
        session.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        session.Inventory[InventoryConstants.SlotMax].Count = 1;
        session.Inventory[InventoryConstants.SlotMax].Durability = 50;

        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte(InputSub);
        packet.WriteInt(1000);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteInt(1);
        await provider.GetRequiredService<IWarehousePacketCoordinator>().HandleAsync(client, packet);

        var reply = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_WAREHOUSE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be(InputSub);
        return (session, reply.ReadByte());
    }
}
