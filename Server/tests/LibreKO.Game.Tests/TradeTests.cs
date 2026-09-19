using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class TradeTests : GameTestBase
{
    [Fact]
    public async Task MerchantPacketCoordinator_OpenAddConfirm_AcceptsAnUnnamedShop()
    {
        const int itemId = 700001000;
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(itemId).Returns(new ItemData
            {
                Num = itemId,
                Countable = 1,
                Duration = 30
            }));

        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 70123, accountId: 41);
        session.Level = 50;
        session.Hp = 100;
        var bagSlot = session.Inventory[InventoryConstants.SlotMax];
        bagSlot.ItemId = itemId;
        bagSlot.Count = 1;
        bagSlot.Durability = 30;

        var coordinator = provider.GetRequiredService<IMerchantPacketCoordinator>();

        var open = new Packet(GameOpcodes.GS_MERCHANT);
        open.WriteByte((byte)MerchantSubOpcode.Open);
        open.ResetOffset();
        await coordinator.HandleAsync(client, open);

        var add = new Packet(GameOpcodes.GS_MERCHANT);
        add.WriteByte((byte)MerchantSubOpcode.ItemAdd);
        add.WriteInt(itemId);
        add.WriteUShort(1);
        add.WriteInt(500);
        add.WriteByte(0);
        add.WriteByte(0);
        add.ResetOffset();
        await coordinator.HandleAsync(client, add);

        session.Trade.MerchantItems[0]!.ItemId.Should().Be(itemId);

        var insert = new Packet(GameOpcodes.GS_MERCHANT);
        insert.WriteByte((byte)MerchantSubOpcode.Insert);
        insert.WriteString(string.Empty);
        insert.ResetOffset();
        await coordinator.HandleAsync(client, insert);

        var reply = sent.LastOrDefault(packet =>
        {
            packet.ResetOffset();
            return packet.GetOpcode() == (byte)GameOpcodes.GS_MERCHANT
                && packet.ReadByte() == (byte)MerchantSubOpcode.Insert;
        });

        reply.Should().NotBeNull("the owner must receive the insert reply or the stall never opens");
        reply!.ResetOffset();
        reply.ReadByte();
        reply.ReadShort().Should().Be(1);
        reply.ReadString().Should().BeEmpty();
        reply.ReadInt().Should().Be(70123);
    }

    [Fact]
    public async Task MerchantPacketCoordinator_HandleSessionEndedAsync_LeavesTheStagedItemInTheInventory()
    {
        const int itemId = 700001000;
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(itemId).Returns(new ItemData
            {
                Num = itemId,
                Countable = 1,
                Duration = 30
            }));

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 31, accountId: 41);
        var bagSlot = session.Inventory[InventoryConstants.SlotMax];
        bagSlot.ItemId = itemId;
        bagSlot.Count = 3;
        bagSlot.Durability = 25;

        var add = new Packet(GameOpcodes.GS_MERCHANT);
        add.WriteByte((byte)MerchantSubOpcode.ItemAdd);
        add.WriteInt(itemId);
        add.WriteUShort(3);
        add.WriteInt(1000);
        add.WriteByte(0);
        add.WriteByte(0);
        add.ResetOffset();

        var coordinator = provider.GetRequiredService<IMerchantPacketCoordinator>();
        await coordinator.HandleAsync(client, add);

        session.Trade.MerchantItems[0]!.ItemId.Should().Be(itemId);
        session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(itemId);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(3);

        await coordinator.HandleSessionEndedAsync(session);

        session.Trade.MerchantState.Should().Be(MerchantMode.None);
        session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(itemId);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(3);
        (session.Trade.MerchantItems[0] == null || session.Trade.MerchantItems[0]!.IsEmpty).Should().BeTrue();
    }

    [Fact]
    public async Task MerchantPacketCoordinator_HandleAsync_BuysFromListedMerchantInsteadOfFirstMatchingMerchant()
    {
        const int itemId = 700001000;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1,
                    Duration = 30
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();

        var buyerClient = Substitute.For<IClient>();
        buyerClient.Id.Returns(Guid.NewGuid());
        buyerClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var buyer = sessionManager.CreateSession(buyerClient, characterId: 51, accountId: 61);
        buyer.Money = 100_000;

        var merchantAClient = Substitute.For<IClient>();
        merchantAClient.Id.Returns(Guid.NewGuid());
        merchantAClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var merchantA = sessionManager.CreateSession(merchantAClient, characterId: 52, accountId: 62);
        merchantA.Trade.MerchantState = MerchantMode.Selling;
        merchantA.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = itemId,
            Count = 1,
            Durability = 10,
            Price = 1000,
            OriginalSlot = InventoryConstants.SlotMax
        };

        var merchantBClient = Substitute.For<IClient>();
        merchantBClient.Id.Returns(Guid.NewGuid());
        merchantBClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var merchantB = sessionManager.CreateSession(merchantBClient, characterId: 53, accountId: 63);
        merchantB.Trade.MerchantState = MerchantMode.Selling;
        merchantB.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = itemId,
            Count = 1,
            Durability = 20,
            Price = 2000,
            OriginalSlot = InventoryConstants.SlotMax
        };

        var coordinator = provider.GetRequiredService<IMerchantPacketCoordinator>();

        var listPacket = new Packet(GameOpcodes.GS_MERCHANT);
        listPacket.WriteByte(5);
        listPacket.WriteInt(merchantB.CharacterId);
        await coordinator.HandleAsync(buyerClient, listPacket);

        var buyPacket = new Packet(GameOpcodes.GS_MERCHANT);
        buyPacket.WriteByte(6);
        buyPacket.WriteInt(itemId);
        buyPacket.WriteUShort(1);
        buyPacket.WriteByte(0);
        buyPacket.WriteByte(0);
        await coordinator.HandleAsync(buyerClient, buyPacket);

        buyer.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(itemId);
        buyer.Inventory[InventoryConstants.SlotMax].Durability.Should().Be(20);
        merchantA.Money.Should().Be(0);
        merchantB.Money.Should().Be(2000);
    }

    [Fact]
    public async Task MerchantPacketCoordinator_HandleAsync_BuyRecalculatesAndSendsBuyerWeightChange()
    {
        const int itemId = 700001004;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetCoefficient(101).Returns(CreateBasicCoefficient(101));
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1,
                    Duration = 20,
                    Weight = 12
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();

        var buyerPackets = new List<Packet>();
        var buyerClient = Substitute.For<IClient>();
        buyerClient.Id.Returns(Guid.NewGuid());
        buyerClient.SendPacket(Arg.Do<Packet>(packet => buyerPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var buyer = sessionManager.CreateSession(buyerClient, characterId: 511, accountId: 611);
        buyer.Class = 101;
        buyer.Level = 20;
        buyer.Strength = 50;
        buyer.Money = 100_000;
        buyer.RecalculateStats(CreateBasicCoefficient(101), provider.GetRequiredService<IGameDataService>());

        var merchantClient = Substitute.For<IClient>();
        merchantClient.Id.Returns(Guid.NewGuid());
        merchantClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var merchant = sessionManager.CreateSession(merchantClient, characterId: 512, accountId: 612);
        merchant.Trade.MerchantState = MerchantMode.Selling;
        merchant.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = itemId,
            Count = 1,
            Durability = 20,
            Price = 2000,
            OriginalSlot = InventoryConstants.SlotMax
        };

        var coordinator = provider.GetRequiredService<IMerchantPacketCoordinator>();

        var listPacket = new Packet(GameOpcodes.GS_MERCHANT);
        listPacket.WriteByte(5);
        listPacket.WriteInt(merchant.CharacterId);
        await coordinator.HandleAsync(buyerClient, listPacket);

        var buyPacket = new Packet(GameOpcodes.GS_MERCHANT);
        buyPacket.WriteByte(6);
        buyPacket.WriteInt(itemId);
        buyPacket.WriteUShort(1);
        buyPacket.WriteByte(0);
        buyPacket.WriteByte(0);
        await coordinator.HandleAsync(buyerClient, buyPacket);

        buyer.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(itemId);
        buyer.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        buyer.Stats.ItemWeight.Should().Be(12);

        var weightPacket = buyerPackets.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WEIGHT_CHANGE);
        weightPacket.ResetOffset();
        weightPacket.ReadInt().Should().Be(12);
    }

    [Fact]
    public async Task MerchantPacketCoordinator_HandleAsync_OpenWhileAlreadyMerchantingClosesDesyncedMerchant()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 54, accountId: 64);
        session.Level = 50;
        session.Hp = 100;
        session.Trade.MerchantState = MerchantMode.Selling;
        var staged = session.Inventory[InventoryConstants.SlotMax];
        staged.ItemId = 700001000;
        staged.Count = 2;
        staged.Durability = 15;
        session.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = 700001000,
            Count = 2,
            Durability = 15,
            OriginalSlot = (byte)InventoryConstants.SlotMax
        };

        var coordinator = provider.GetRequiredService<IMerchantPacketCoordinator>();
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte(1);
        packet.ResetOffset();

        await coordinator.HandleAsync(client, packet);

        session.Trade.IsMerchanting.Should().BeFalse();
        session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(700001000);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(2);
        (session.Trade.MerchantItems[0] == null || session.Trade.MerchantItems[0]!.IsEmpty).Should().BeTrue();
    }

    [Fact]
    public async Task MerchantPacketCoordinator_HandleAsync_AddRejectsSealedItem()
    {
        const int itemId = 700001001;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1
                });
            });

        Packet? sentPacket = null;
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 55, accountId: 65);
        session.Hp = 100;
        session.Level = 50;
        session.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        session.Inventory[InventoryConstants.SlotMax].Count = 5;
        session.Inventory[InventoryConstants.SlotMax].Durability = 20;
        session.Inventory[InventoryConstants.SlotMax].Flag = 4;

        var coordinator = provider.GetRequiredService<IMerchantPacketCoordinator>();
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte(3);
        packet.WriteInt(itemId);
        packet.WriteUShort(2);
        packet.WriteInt(1000);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.ResetOffset();

        await coordinator.HandleAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be((byte)MerchantSubOpcode.ItemAdd);
        sentPacket.ReadShort().Should().Be((short)MerchantResult.CannotTrade);
        (session.Trade.MerchantItems[0] == null || session.Trade.MerchantItems[0]!.IsEmpty).Should().BeTrue();
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(5);
    }

    [Fact]
    public async Task ExchangePacketCoordinator_HandleAsync_RequestNamesTheAskerInFull()
    {
        const int askerId = 70001;
        const int targetId = 70002;

        using var provider = CreateProvider(_ => { });
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var askerClient = Substitute.For<IClient>();
        askerClient.Id.Returns(Guid.NewGuid());
        askerClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        Packet? sentPacket = null;
        var targetClient = Substitute.For<IClient>();
        targetClient.Id.Returns(Guid.NewGuid());
        targetClient.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var asker = sessionManager.CreateSession(askerClient, askerId, accountId: 80001);
        var target = sessionManager.CreateSession(targetClient, targetId, accountId: 80002);
        foreach (var session in new[] { asker, target })
        {
            session.Hp = 100;
            session.Nation = AccountNation.Karus;
            session.ZoneId = BattleZoneManager.ZONE_MORADON;
            session.X = 816f;
            session.Z = 531f;
        }

        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(1);
        packet.WriteInt(targetId);
        packet.WriteByte(1);
        packet.ResetOffset();

        await provider.GetRequiredService<IExchangePacketCoordinator>()
            .HandleAsync(askerClient, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadInt().Should().Be(askerId, "a character id does not fit in two bytes");
    }

    [Fact]
    public async Task ExchangePacketCoordinator_HandleAsync_RequestRejectsSelfTrade()
    {
        using var provider = CreateProvider(_ => { });

        Packet? sentPacket = null;
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 56, accountId: 66);
        session.Hp = 100;

        var coordinator = provider.GetRequiredService<IExchangePacketCoordinator>();
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(1);
        packet.WriteInt(session.CharacterId);
        packet.ResetOffset();

        await coordinator.HandleAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(8);
        session.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task ExchangePacketCoordinator_HandleAsync_AddRejectsSealedItem()
    {
        const int itemId = 700001002;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1,
                    Weight = 1
                });
            });

        Packet? sentPacket = null;
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var targetClient = Substitute.For<IClient>();
        targetClient.Id.Returns(Guid.NewGuid());
        targetClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 57, accountId: 67);
        var target = sessionManager.CreateSession(targetClient, characterId: 58, accountId: 68);
        session.Hp = 100;
        target.Hp = 100;
        session.Trade.ExchangeUser = target.CharacterId;
        target.Trade.ExchangeUser = session.CharacterId;
        session.InitExchange(true);
        target.InitExchange(true);
        session.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        session.Inventory[InventoryConstants.SlotMax].Count = 3;
        session.Inventory[InventoryConstants.SlotMax].Durability = 7;
        session.Inventory[InventoryConstants.SlotMax].Flag = 4;

        var coordinator = provider.GetRequiredService<IExchangePacketCoordinator>();
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(3);
        packet.WriteByte(0);
        packet.WriteInt(itemId);
        packet.WriteInt(1);
        packet.ResetOffset();

        await coordinator.HandleAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(3);
        sentPacket.ReadByte().Should().Be(0);
        session.Trade.ExchangeItemList.Should().BeEmpty();
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(3);
    }

    [Fact]
    public async Task ExchangePacketCoordinator_HandleAsync_DecideFailsWhenReceiverWouldBeOverweight()
    {
        const int itemId = 700001003;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 1,
                    Weight = 10
                });
            });

        var giverPackets = new List<Packet>();
        var giverClient = Substitute.For<IClient>();
        giverClient.Id.Returns(Guid.NewGuid());
        giverClient.SendPacket(Arg.Do<Packet>(packet => giverPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var receiverPackets = new List<Packet>();
        var receiverClient = Substitute.For<IClient>();
        receiverClient.Id.Returns(Guid.NewGuid());
        receiverClient.SendPacket(Arg.Do<Packet>(packet => receiverPackets.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var giver = sessionManager.CreateSession(giverClient, characterId: 59, accountId: 69);
        var receiver = sessionManager.CreateSession(receiverClient, characterId: 60, accountId: 70);
        giver.Hp = 100;
        receiver.Hp = 100;
        giver.Trade.ExchangeUser = receiver.CharacterId;
        receiver.Trade.ExchangeUser = giver.CharacterId;
        giver.InitExchange(true);
        receiver.InitExchange(true);
        giver.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        giver.Inventory[InventoryConstants.SlotMax].Count = 1;
        giver.Inventory[InventoryConstants.SlotMax].Durability = 10;
        receiver.Stats = new DerivedStats
        {
            ItemWeight = 95,
            MaxWeight = 100
        };
        receiver.Trade.ExchangeOk = true;

        var coordinator = provider.GetRequiredService<IExchangePacketCoordinator>();

        var addPacket = new Packet(GameOpcodes.GS_EXCHANGE);
        addPacket.WriteByte(3);
        addPacket.WriteByte(0);
        addPacket.WriteInt(itemId);
        addPacket.WriteInt(1);
        addPacket.ResetOffset();
        await coordinator.HandleAsync(giverClient, addPacket);

        var decidePacket = new Packet(GameOpcodes.GS_EXCHANGE);
        decidePacket.WriteByte(5);
        decidePacket.ResetOffset();
        await coordinator.HandleAsync(giverClient, decidePacket);

        var giverFail = giverPackets.Single(packet =>
        {
            packet.ResetOffset();
            return packet.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE
                && packet.ReadByte() == 7
                && packet.ReadByte() == 0;
        });
        var receiverFail = receiverPackets.Single(packet =>
        {
            packet.ResetOffset();
            return packet.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE
                && packet.ReadByte() == 7
                && packet.ReadByte() == 0;
        });

        giverFail.Should().NotBeNull();
        receiverFail.Should().NotBeNull();
        giver.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(itemId);
        giver.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        receiver.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task WarehousePacketCoordinator_HandleAsync_RejectsMergingNonStackableItems()
    {
        const int itemId = 800001000;
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetItem(itemId).Returns(new ItemData
                {
                    Num = itemId,
                    Countable = 0,
                    Duration = 50
                });
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 71, accountId: 81);
        session.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        session.Inventory[InventoryConstants.SlotMax].Count = 1;
        session.Inventory[InventoryConstants.SlotMax].Durability = 50;
        session.Warehouse[0].ItemId = itemId;
        session.Warehouse[0].Count = 1;
        session.Warehouse[0].Durability = 50;

        // Wire layout (Input 2): [u8 opcode][u32 npcId][u32 itemId][u8 page][u8 src][u8 dst][i32 count].
        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte(2);
        packet.WriteInt(1000);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteInt(1);

        var coordinator = provider.GetRequiredService<IWarehousePacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(2);
        sentPacket.ReadByte().Should().Be(0);
        session.Inventory[InventoryConstants.SlotMax].Count.Should().Be(1);
        session.Warehouse[0].Count.Should().Be(1);
    }

    [Fact]
    public async Task WarehousePacketCoordinator_HandleAsync_OpenIncludesLegacySlotPayload()
    {
        const int firstItemId = 800001001;
        const int secondItemId = 800001002;

        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 72, accountId: 82);
        session.WarehouseMoney = 12345;
        session.Warehouse[0].ItemId = firstItemId;
        session.Warehouse[0].Durability = 50;
        session.Warehouse[0].Count = 2;
        session.Warehouse[0].Flag = 3;
        session.Warehouse[1].ItemId = secondItemId;
        session.Warehouse[1].Durability = 20;
        session.Warehouse[1].Count = 1;
        session.Warehouse[1].Flag = 4;

        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte(1);

        var coordinator = provider.GetRequiredService<IWarehousePacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadByte().Should().Be(1);
        sentPacket.ReadInt().Should().Be(12345);

        sentPacket.ReadInt().Should().Be(firstItemId);
        sentPacket.ReadShort().Should().Be(50);
        sentPacket.ReadUShort().Should().Be(2);
        sentPacket.ReadByte().Should().Be(3);
        sentPacket.ReadULong().Should().Be(0);

        sentPacket.ReadInt().Should().Be(secondItemId);
        sentPacket.ReadShort().Should().Be(20);
        sentPacket.ReadUShort().Should().Be(1);
        sentPacket.ReadByte().Should().Be(4);
        sentPacket.ReadULong().Should().Be(0);
    }

    [Fact]
    public async Task WarehousePacketCoordinator_HandleAsync_InputStoresGold()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 73, accountId: 83);
        session.Money = 5000;
        session.WarehouseMoney = 1000;

        // Wire layout (Input 2): [u8 opcode][u32 npcId][u32 itemId][u8 page][u8 src][u8 dst][i32 count].
        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte(2);
        packet.WriteInt(1000);          // npc id
        packet.WriteInt(900000000);     // item id (Gold sentinel)
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteInt(2000);

        var coordinator = provider.GetRequiredService<IWarehousePacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(2);
        sentPacket.ReadByte().Should().Be(1);
        session.Money.Should().Be(3000);
        session.WarehouseMoney.Should().Be(3000);
    }

    [Fact]
    public async Task WarehousePacketCoordinator_HandleAsync_OutputWithdrawsGold()
    {
        using var provider = CreateProvider(_ => { });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        Packet? sentPacket = null;
        client.SendPacket(Arg.Do<Packet>(packet => sentPacket = packet), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 74, accountId: 84);
        session.Money = 1000;
        session.WarehouseMoney = 5000;

        // Wire layout (Output 3): [u8 opcode][u32 npcId][u32 itemId][u8 page][u8 src][u8 dst][i32 count].
        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte(3);
        packet.WriteInt(1000);
        packet.WriteInt(900000000);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.WriteInt(2000);

        var coordinator = provider.GetRequiredService<IWarehousePacketCoordinator>();
        await coordinator.HandleAsync(client, packet);

        sentPacket.Should().NotBeNull();
        sentPacket!.ResetOffset();
        sentPacket.ReadByte().Should().Be(3);
        sentPacket.ReadByte().Should().Be(1);
        session.Money.Should().Be(3000);
        session.WarehouseMoney.Should().Be(3000);
    }

    private const byte ExchangeAddSub = 3;
    private const float OutOfTradeRangeOffset = 1000f;

    [Fact]
    public async Task ExchangeTransferService_AddAsync_RejectsNegativeCountInsteadOfInflatingStack()
    {
        const int itemId = 700001000;
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 1, Duration = 30 }));

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (giver, giverPackets) = CreateTradingSession(sessionManager, 8101, 8201);
        var (taker, _) = CreateTradingSession(sessionManager, 8102, 8202);
        PairForTrade(giver, taker);

        giver.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        giver.Inventory[InventoryConstants.SlotMax].Count = 5;

        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(0);
        packet.WriteInt(itemId);
        packet.WriteInt(-1);

        await provider.GetRequiredService<IExchangeTransferService>().AddAsync(giver, packet);

        giver.Inventory[InventoryConstants.SlotMax].Count.Should().Be(5);
        giver.Trade.ExchangeItemList.Should().BeEmpty();

        var reply = giverPackets.Single(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE);
        reply.ResetOffset();
        reply.ReadByte().Should().Be(ExchangeAddSub);
        reply.ReadByte().Should().Be(0);
    }

    [Fact]
    public async Task ExchangeTransferService_AddAsync_CancelsWhenPartnerWalkedOutOfRange()
    {
        const int itemId = 700001000;
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 1, Duration = 30 }));

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (giver, _) = CreateTradingSession(sessionManager, 8103, 8203);
        var (taker, _) = CreateTradingSession(sessionManager, 8104, 8204);
        PairForTrade(giver, taker);

        taker.X = giver.X + OutOfTradeRangeOffset;

        giver.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        giver.Inventory[InventoryConstants.SlotMax].Count = 5;

        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(0);
        packet.WriteInt(itemId);
        packet.WriteInt(1);

        await provider.GetRequiredService<IExchangeTransferService>().AddAsync(giver, packet);

        giver.Inventory[InventoryConstants.SlotMax].Count.Should().Be(5);
        giver.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task ExchangeTransferService_DecideAsync_DoesNotRefundOfferedGoldToTheGiver()
    {
        using var provider = CreateProvider(_ => { });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (giver, _) = CreateTradingSession(sessionManager, 8105, 8205);
        var (taker, _) = CreateTradingSession(sessionManager, 8106, 8206);
        PairForTrade(giver, taker);
        giver.InitExchange(true);
        taker.InitExchange(true);

        giver.Money = 10_000;
        taker.Money = 0;

        var addGold = new Packet(GameOpcodes.GS_EXCHANGE);
        addGold.WriteByte(0);
        addGold.WriteInt(ExchangeGoldItemId);
        addGold.WriteInt(4_000);

        var transfer = provider.GetRequiredService<IExchangeTransferService>();
        await transfer.AddAsync(giver, addGold);

        giver.Money.Should().Be(6_000);

        await transfer.DecideAsync(taker);
        await transfer.DecideAsync(giver);

        giver.Money.Should().Be(6_000);
        taker.Money.Should().Be(4_000);
        giver.Trade.ExchangeItemList.Should().BeEmpty();
        giver.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task ExchangeTransferService_SecondTrade_DoesNotResendGoldFromTheFirstTrade()
    {
        const int itemId = 700001000;
        using var provider = CreateProvider(
            _ => { },
            gameData => gameData.GetItem(itemId).Returns(new ItemData { Num = itemId, Countable = 1, Duration = 30 }));

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (giver, _) = CreateTradingSession(sessionManager, 8107, 8207);
        var (taker, _) = CreateTradingSession(sessionManager, 8108, 8208);
        var transfer = provider.GetRequiredService<IExchangeTransferService>();

        giver.Money = 10_000;
        taker.Money = 0;
        giver.Inventory[InventoryConstants.SlotMax].ItemId = itemId;
        giver.Inventory[InventoryConstants.SlotMax].Count = 10;

        PairForTrade(giver, taker);
        giver.InitExchange(true);
        taker.InitExchange(true);

        await transfer.AddAsync(giver, AddItemPacket(0, itemId, 1));
        await transfer.AddAsync(giver, AddItemPacket(0, ExchangeGoldItemId, 100));
        await transfer.DecideAsync(taker);
        await transfer.DecideAsync(giver);

        taker.Money.Should().Be(100);

        PairForTrade(giver, taker);
        giver.InitExchange(true);
        taker.InitExchange(true);

        await transfer.AddAsync(giver, AddItemPacket(0, itemId, 1));
        await transfer.DecideAsync(taker);
        await transfer.DecideAsync(giver);

        taker.Money.Should().Be(100);
        giver.Money.Should().Be(9_900);
    }

    private static Packet AddItemPacket(byte pos, int itemId, int count)
    {
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(pos);
        packet.WriteInt(itemId);
        packet.WriteInt(count);
        return packet;
    }

    private const int ExchangeGoldItemId = 900000000;

    private static (UserSession Session, List<Packet> Packets) CreateTradingSession(
        SessionManager sessionManager, int characterId, int accountId)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var packets = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packets.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = sessionManager.CreateSession(client, characterId, accountId);
        session.Hp = 100;
        session.ZoneId = 1;
        session.X = 100f;
        session.Z = 100f;
        return (session, packets);
    }

    private static void PairForTrade(UserSession a, UserSession b)
    {
        a.Trade.ExchangeUser = b.CharacterId;
        b.Trade.ExchangeUser = a.CharacterId;
    }
}
