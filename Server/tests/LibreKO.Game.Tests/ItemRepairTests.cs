using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ItemRepairTests : GameTestBase
{
    private const int SwordId = 110110001;
    private const int MaxDurability = 5000;
    private const int WornDurability = 1000;
    private const int BuyPrice = 20000;
    private const int SaleTypeNormal = 0;
    private const int SaleTypeNoRepair = 2;
    private const byte BagPosition = 2;

    [Fact]
    public async Task AMerchantRepairsAWornItem()
    {
        var (provider, session, _, npc) = Setup(NpcData.TypeTradeMerchant, SaleTypeNormal);
        using (provider)
        {
            await provider.GetRequiredService<IItemPacketCoordinator>().HandleRepairAsync(session.Client, Repair(npc));

            session.Inventory[InventoryConstants.InventoryStart].Durability.Should().Be(MaxDurability);
            session.Money.Should().BeLessThan(1_000_000);
        }
    }

    [Fact]
    public async Task AnItemSoldAsNotRepairableIsRefused()
    {
        var (provider, session, _, npc) = Setup(NpcData.TypeRepairMerchant, SaleTypeNoRepair);
        using (provider)
        {
            await provider.GetRequiredService<IItemPacketCoordinator>().HandleRepairAsync(session.Client, Repair(npc));

            session.Inventory[InventoryConstants.InventoryStart].Durability.Should().Be(WornDurability);
            session.Money.Should().Be(1_000_000);
        }
    }

    [Fact]
    public async Task OnlyMerchantsAndTinkersRepair()
    {
        var (provider, session, _, npc) = Setup(NpcData.TypeWarehouse, SaleTypeNormal);
        using (provider)
        {
            await provider.GetRequiredService<IItemPacketCoordinator>().HandleRepairAsync(session.Client, Repair(npc));

            session.Inventory[InventoryConstants.InventoryStart].Durability.Should().Be(WornDurability);
            session.Money.Should().Be(1_000_000);
        }
    }

    private static Packet Repair(NpcInstance npc)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_REPAIR);
        packet.WriteByte(BagPosition);
        packet.WriteByte(0);
        packet.WriteInt(npc.UniqueId);
        packet.WriteInt(SwordId);
        return packet;
    }

    private static (ServiceProvider Provider, UserSession Session, List<Packet> Sent, NpcInstance Npc) Setup(byte npcType, int saleType)
    {
        var provider = CreateProvider(_ => { }, gameData =>
            gameData.GetItem(SwordId).Returns(new ItemData
            {
                Num = SwordId,
                BuyPrice = BuyPrice,
                Duration = MaxDurability,
                SellPrice = saleType,
            }));

        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 471, accountId: 481);
        session.Hp = 100;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Money = 1_000_000;
        session.Inventory[InventoryConstants.InventoryStart].ItemId = SwordId;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;
        session.Inventory[InventoryConstants.InventoryStart].Durability = WornDurability;

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 5002,
            ZoneId = 1,
            X = 10,
            Z = 10,
            MaxHp = 1,
            Hp = 1,
            NpcType = npcType,
        });
        return (provider, session, sent, npc);
    }
}
