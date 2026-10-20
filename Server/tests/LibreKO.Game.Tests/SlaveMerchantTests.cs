using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class SlaveMerchantTests : GameTestBase
{
    private const int ItemId = 379080000;

    [Fact]
    public async Task Player_EnablingSlaveMerchant_ClonesToSlaveBotSession_DeductsInventory_AndReleasesPlayer()
    {
        using var provider = CreateProvider(
            seed: _ => { },
            configureGameData: gameData =>
            {
                gameData.GetItem(ItemId).Returns(new ItemData
                {
                    Num = ItemId,
                    Name = "Special Scroll",
                    Countable = 1,
                    Duration = 5000,
                    BuyPrice = 500,
                    Weight = 10,
                });
                gameData.GetItem(700111000).Returns(new ItemData
                {
                    Num = 700111000,
                    Name = "Voucher of Offline Merchant",
                    Countable = 1,
                    Duration = 1,
                    BuyPrice = 500,
                    Weight = 1,
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var lifecycleService = provider.GetRequiredService<IMerchantLifecycleService>();
        var adminCoordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        // Set up regular player session
        var player = CreatePlayer(sessionManager, characterId: 2001, accountId: 3001, out _);
        player.Name = "TraderHero";
        player.ZoneId = 21;
        player.X = 500f;
        player.Y = 0f;
        player.Z = 400f;

        // Give player item in inventory
        var slotIndex = InventoryConstants.InventoryStart;
        player.Inventory[slotIndex].ItemId = ItemId;
        player.Inventory[slotIndex].Count = 10;
        player.Inventory[slotIndex].Durability = 5000;
        player.Inventory[slotIndex + 1].ItemId = 700111000;
        player.Inventory[slotIndex + 1].Count = 1;

        // Player activates slave merchant mode
        await adminCoordinator.HandleGmCommandAsync(player, "+slave");
        player.IsSlaveMerchantMode.Should().BeTrue();

        // Stage merchant items
        player.Trade.IsSellingMerchantPreparing = true;
        player.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 4,
            Price = 1500,
            Durability = 5000,
            OriginalSlot = (byte)slotIndex,
        };

        // Player clicks confirm
        var insertPacket = new Packet(GameOpcodes.GS_MERCHANT);
        insertPacket.WriteString("Best Slave Deals");
        await lifecycleService.InsertAsync(player, insertPacket);

        // Player should be released (not merchanting anymore, slave mode consumed)
        player.Trade.IsMerchanting.Should().BeFalse();
        player.IsSlaveMerchantMode.Should().BeFalse();

        // Inventory should have 6 items left (10 - 4 = 6)
        player.Inventory[slotIndex].Count.Should().Be(6);

        // A SlaveMerchantBotSession should now exist
        var slaveBots = sessionManager.GetAll().OfType<SlaveMerchantBotSession>().ToList();
        slaveBots.Should().HaveCount(1);

        var slave = slaveBots.Single();
        slave.OwnerCharacterId.Should().Be(player.CharacterId);
        slave.Name.Should().Be("[S]TraderHero");
        slave.Trade.IsMerchanting.Should().BeTrue();
        slave.Trade.MerchantAdvert.Should().Be("Best Slave Deals");
        slave.Trade.MerchantItems[0].Should().NotBeNull();
        slave.Trade.MerchantItems[0]!.Count.Should().Be(4);
        slave.Trade.MerchantItems[0]!.Price.Should().Be(1500);

        // Database record should exist
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbRecord = await db.SlaveMerchants.FirstOrDefaultAsync(s => s.OwnerCharacterId == player.CharacterId);
        dbRecord.Should().NotBeNull();
        dbRecord!.IsActive.Should().BeTrue();
        dbRecord.EarnedNoah.Should().Be(0);
    }

    [Fact]
    public async Task BuyerPurchasesFromSlaveBot_TransfersNoahToEarnedNoah_AndCanBeClaimed()
    {
        using var provider = CreateProvider(
            seed: _ => { },
            configureGameData: gameData =>
            {
                gameData.GetItem(ItemId).Returns(new ItemData
                {
                    Num = ItemId,
                    Name = "Special Scroll",
                    Countable = 1,
                    Duration = 5000,
                    BuyPrice = 500,
                    Weight = 10,
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var lifecycleService = provider.GetRequiredService<IMerchantLifecycleService>();
        var listingService = provider.GetRequiredService<IMerchantListingService>();
        var adminCoordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        // Set up owner
        var owner = CreatePlayer(sessionManager, characterId: 2002, accountId: 3002, out _);
        owner.Name = "StoreOwner";
        owner.ZoneId = 21;
        owner.Money = 10_000;

        var slotIndex = InventoryConstants.InventoryStart;
        owner.Inventory[slotIndex].ItemId = ItemId;
        owner.Inventory[slotIndex].Count = 10;
        owner.Inventory[slotIndex].Durability = 5000;
        owner.Inventory[slotIndex + 1].ItemId = 700111000;
        owner.Inventory[slotIndex + 1].Count = 1;

        await adminCoordinator.HandleGmCommandAsync(owner, "+slave");

        owner.Trade.IsSellingMerchantPreparing = true;
        owner.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 5,
            Price = 2000,
            Durability = 5000,
            OriginalSlot = (byte)slotIndex,
        };

        var insertPacket = new Packet(GameOpcodes.GS_MERCHANT);
        insertPacket.WriteString("Scroll Shop");
        await lifecycleService.InsertAsync(owner, insertPacket);

        var slave = sessionManager.GetAll().OfType<SlaveMerchantBotSession>().Single();

        // Set up buyer
        var buyer = CreatePlayer(sessionManager, characterId: 2003, accountId: 3003, out _, money: 50_000);
        buyer.Name = "RichBuyer";
        buyer.ZoneId = 21;

        // Buyer opens merchant list
        var listPacket = new Packet(GameOpcodes.GS_MERCHANT);
        listPacket.WriteInt(slave.CharacterId);
        await listingService.ListItemsAsync(buyer, listPacket);

        // Buyer buys 2 scrolls (2 * 2000 = 4000 gold)
        var buyPacket = new Packet(GameOpcodes.GS_MERCHANT);
        buyPacket.WriteInt(ItemId);
        buyPacket.WriteUShort(2);
        buyPacket.WriteByte(0); // merchantSlot 0
        buyPacket.WriteByte(0); // buyerSlot

        await listingService.BuyItemAsync(buyer, buyPacket);

        buyer.Money.Should().Be(50_000 - 4000);
        slave.Trade.MerchantItems[0]!.Count.Should().Be(3);

        // Check DB earned noah
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbRecord = await db.SlaveMerchants.FirstAsync(s => s.Id == slave.SlaveDatabaseId);
        dbRecord.EarnedNoah.Should().Be(4000);

        // Owner claims earnings via +slaveclaim
        await adminCoordinator.HandleGmCommandAsync(owner, "+slaveclaim");
        owner.Money.Should().Be(10_000 + 4000);

        // DB record should be reset to 0
        db.Entry(dbRecord).Reload();
        dbRecord.EarnedNoah.Should().Be(0);
    }

    [Fact]
    public async Task PlayerClosesSlaveMerchant_DespawnsBot_AndReturnsUnsoldItems()
    {
        using var provider = CreateProvider(
            seed: _ => { },
            configureGameData: gameData =>
            {
                gameData.GetItem(ItemId).Returns(new ItemData
                {
                    Num = ItemId,
                    Name = "Special Scroll",
                    Countable = 1,
                    Duration = 5000,
                    BuyPrice = 500,
                    Weight = 10,
                });
            });

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var lifecycleService = provider.GetRequiredService<IMerchantLifecycleService>();
        var adminCoordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        var owner = CreatePlayer(sessionManager, characterId: 2004, accountId: 3004, out _);
        owner.Name = "ClosingOwner";
        owner.ZoneId = 21;
        owner.Money = 5000;

        var slotIndex = InventoryConstants.InventoryStart;
        owner.Inventory[slotIndex].ItemId = ItemId;
        owner.Inventory[slotIndex].Count = 5;
        owner.Inventory[slotIndex].Durability = 5000;
        owner.Inventory[slotIndex + 1].ItemId = 700111000;
        owner.Inventory[slotIndex + 1].Count = 1;

        await adminCoordinator.HandleGmCommandAsync(owner, "+slave");

        owner.Trade.IsSellingMerchantPreparing = true;
        owner.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 5,
            Price = 1000,
            Durability = 5000,
            OriginalSlot = (byte)slotIndex,
        };

        var insertPacket = new Packet(GameOpcodes.GS_MERCHANT);
        insertPacket.WriteString("Temporary Stall");
        await lifecycleService.InsertAsync(owner, insertPacket);

        // Inventory was emptied when slave was created
        owner.Inventory[slotIndex].IsEmpty.Should().BeTrue();

        // Close slave merchant via +slaveclose
        await adminCoordinator.HandleGmCommandAsync(owner, "+slaveclose");

        // Slave bot should be despawned from session manager
        sessionManager.GetAll().OfType<SlaveMerchantBotSession>().Should().BeEmpty();

        // Items should be returned to owner inventory
        owner.Inventory[slotIndex].ItemId.Should().Be(ItemId);
        owner.Inventory[slotIndex].Count.Should().Be(5);

        // DB record should be inactive
        using var scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbRecord = await db.SlaveMerchants.FirstAsync(s => s.OwnerCharacterId == owner.CharacterId);
        dbRecord.IsActive.Should().BeFalse();
    }

    private static UserSession CreatePlayer(
        SessionManager sessionManager, int characterId, int accountId, out List<Packet> sentPackets, int money = 0)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var captured = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(captured.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        sentPackets = captured;

        var session = sessionManager.CreateSession(client, characterId, accountId);
        session.ZoneId = 21;
        session.Level = 80;
        session.MaxHp = 5000;
        session.Hp = 5000;
        session.Money = money;
        sessionManager.Regions.AddToRegion(session);
        return session;
    }
}
