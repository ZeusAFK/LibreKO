using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class LoyaltyMerchantTests : GameTestBase
{
    private const int LoyaltyGroup = 249000;
    private const int ItemId = 700303;
    private const int Price = 300;

    [Fact]
    public async Task TheLoyaltyMerchantChargesNationalPointsAndLeavesTheCoins()
    {
        var (provider, session, sent, npc) = Setup();
        using (provider)
        {
            session.Money = 100;
            session.Loyalty = 1000;

            await provider.GetRequiredService<IItemPacketCoordinator>().HandleTradeAsync(session.Client, Buy(npc, count: 2));

            session.Money.Should().Be(100);
            session.Loyalty.Should().Be(1000 - 2 * Price);
            session.Inventory[InventoryConstants.InventoryStart].ItemId.Should().Be(ItemId);
            var reply = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_TRADE);
            reply.ResetOffset();
            reply.ReadByte().Should().Be(1);
            reply.ReadInt().Should().Be(1000 - 2 * Price);
            reply.ReadInt().Should().Be(2 * Price);
            reply.RemainingBytes.Should().Be(1);
        }
    }

    [Fact]
    public async Task TheLoyaltyMerchantRefusesWhenNationalPointsAreShortEvenWithCoins()
    {
        var (provider, session, sent, npc) = Setup();
        using (provider)
        {
            session.Money = 1_000_000;
            session.Loyalty = Price - 1;

            await provider.GetRequiredService<IItemPacketCoordinator>().HandleTradeAsync(session.Client, Buy(npc, count: 1));

            session.Money.Should().Be(1_000_000);
            session.Loyalty.Should().Be(Price - 1);
            session.Inventory[InventoryConstants.InventoryStart].IsEmpty.Should().BeTrue();
            var reply = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_TRADE);
            reply.ResetOffset();
            reply.ReadByte().Should().NotBe(1);
        }
    }

    [Fact]
    public async Task TheLoyaltyMerchantBuysNothingBack()
    {
        var (provider, session, sent, npc) = Setup();
        using (provider)
        {
            session.Inventory[InventoryConstants.InventoryStart].ItemId = ItemId;
            session.Inventory[InventoryConstants.InventoryStart].Count = 2;
            session.Inventory[InventoryConstants.InventoryStart].Durability = 1;

            var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
            packet.WriteByte(2);
            packet.WriteInt(LoyaltyGroup);
            packet.WriteInt(npc.UniqueId);
            packet.WriteByte(1);
            packet.WriteInt(ItemId);
            packet.WriteByte(0);
            packet.WriteUShort(2);
            await provider.GetRequiredService<IItemPacketCoordinator>().HandleTradeAsync(session.Client, packet);

            session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(2);
            session.Money.Should().Be(0);
            var reply = sent.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_TRADE);
            reply.ResetOffset();
            reply.ReadByte().Should().NotBe(1);
        }
    }

    private static Packet Buy(NpcInstance npc, ushort count)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(1);
        packet.WriteInt(LoyaltyGroup);
        packet.WriteInt(npc.UniqueId);
        packet.WriteByte(1);
        packet.WriteInt(ItemId);
        packet.WriteByte(0);
        packet.WriteUShort(count);
        packet.WriteByte(0);
        packet.WriteByte(0);
        return packet;
    }

    private static (ServiceProvider Provider, UserSession Session, List<Packet> Sent, NpcInstance Npc) Setup()
    {
        var provider = CreateProvider(_ => { }, gameData =>
            gameData.GetItem(ItemId).Returns(new ItemData
            {
                Num = ItemId,
                BuyPrice = Price,
                Countable = 1,
                Duration = 1,
            }));

        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 470, accountId: 480);
        session.Hp = 100;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 5001,
            SellingGroup = LoyaltyGroup,
            ZoneId = 1,
            X = 10,
            Z = 10,
            MaxHp = 1,
            Hp = 1,
            NpcType = 11,
        });
        return (provider, session, sent, npc);
    }
}
