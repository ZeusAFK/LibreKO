using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ItemRemoveTests : GameTestBase
{
    private const int Sword = 110110001;
    private const byte FirstBagSlot = 0;
    private const short AliveHp = 100;

    [Theory]
    [InlineData(ItemFlag.Rented)]
    [InlineData(ItemFlag.Sealed)]
    public async Task ARentedOrSealedItemIsNotDestroyed(ItemFlag state)
    {
        using var provider = CreateProvider(_ => { });
        var session = Holder(provider, state);

        await provider.GetRequiredService<IItemRemoveService>().HandleAsync(session.Client, Remove(Sword));

        session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(Sword);
    }

    [Fact]
    public async Task AnOrdinaryItemIsDestroyed()
    {
        using var provider = CreateProvider(_ => { });
        var session = Holder(provider, ItemFlag.Unsealed);

        await provider.GetRequiredService<IItemRemoveService>().HandleAsync(session.Client, Remove(Sword));

        session.Inventory[InventoryConstants.InventoryStart].IsEmpty.Should().BeTrue();
    }

    private static Packet Remove(int itemId)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        packet.WriteByte((byte)ItemRemoveSource.Inventory);
        packet.WriteByte(FirstBagSlot);
        packet.WriteInt(itemId);
        return packet;
    }

    private static UserSession Holder(ServiceProvider provider, ItemFlag state)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 612, accountId: 622);
        session.Name = "Holder";
        session.Hp = AliveHp;
        var slot = session.Inventory[InventoryConstants.InventoryStart];
        slot.ItemId = Sword;
        slot.Count = 1;
        slot.Durability = 100;
        slot.Flag = (byte)state;
        return session;
    }
}
