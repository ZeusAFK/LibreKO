using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class MerchantBotTests : GameTestBase
{
    private const int ItemId = 379080000;

    [Fact]
    public async Task GmOpeningSellingMerchant_AutomaticallyClonesToBotSession_AndReleasesGm()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var lifecycleService = provider.GetRequiredService<IMerchantLifecycleService>();

        // Set up GM session
        var gm = CreatePlayer(sessionManager, characterId: 1001, accountId: 2001, out _);
        gm.IsGM = true;
        gm.Name = "GM_Test";
        gm.ZoneId = 21; // Moradon
        gm.X = 500f;
        gm.Y = 0f;
        gm.Z = 400f;

        // Give GM an item in inventory
        var slotIndex = InventoryConstants.InventoryStart;
        gm.Inventory[slotIndex].ItemId = ItemId;
        gm.Inventory[slotIndex].Count = 10;
        gm.Inventory[slotIndex].Durability = 5000;

        // Prepare selling merchant
        gm.Trade.IsSellingMerchantPreparing = true;
        gm.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 5,
            Price = 1000,
            Durability = 5000,
            OriginalSlot = (byte)slotIndex,
        };

        // GM opens stall
        var insertPacket = new Packet(GameOpcodes.GS_MERCHANT);
        insertPacket.WriteString("Great Bot Stall");
        await lifecycleService.InsertAsync(gm, insertPacket);

        // GM must be released (not merchanting anymore)
        gm.Trade.IsMerchanting.Should().BeFalse();

        // A BotSession should now exist in the session manager
        var bots = sessionManager.GetAll().OfType<BotSession>().ToList();
        bots.Should().HaveCount(1);

        var bot = bots.First();
        bot.IsBot.Should().BeTrue();
        bot.Name.Should().StartWith("[B]");
        bot.ZoneId.Should().Be(gm.ZoneId);
        bot.X.Should().Be(gm.X);
        bot.Z.Should().Be(gm.Z);
        bot.Trade.IsMerchanting.Should().BeTrue();
        bot.Trade.MerchantAdvert.Should().Be("Great Bot Stall");
        bot.Trade.MerchantItems[0].Should().NotBeNull();
        bot.Trade.MerchantItems[0]!.ItemId.Should().Be(ItemId);
        bot.Trade.MerchantItems[0]!.Count.Should().Be(5);
        bot.Trade.MerchantItems[0]!.Price.Should().Be(1000);
    }

    [Fact]
    public async Task GmOpeningSellingMerchant_WithUserBotsInDatabase_PicksNameFromUserBotsTable()
    {
        using var provider = CreateProvider(
            seed: db =>
            {
                db.UserBots.Add(new UserBotData { Id = 1, Name = "ShadowTrader", Nation = 0, IsActive = true });
                db.UserBots.Add(new UserBotData { Id = 2, Name = "BazarHero", Nation = 0, IsActive = true });
                db.SaveChanges();
            },
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

        var gm1 = CreatePlayer(sessionManager, characterId: 1005, accountId: 2005, out _);
        gm1.IsGM = true;
        gm1.Inventory[InventoryConstants.InventoryStart].ItemId = ItemId;
        gm1.Inventory[InventoryConstants.InventoryStart].Count = 1;
        gm1.Trade.IsSellingMerchantPreparing = true;
        gm1.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 1,
            Price = 100,
            OriginalSlot = InventoryConstants.InventoryStart,
        };

        var packet1 = new Packet(GameOpcodes.GS_MERCHANT);
        packet1.WriteString("Stall 1");
        await lifecycleService.InsertAsync(gm1, packet1);

        var firstBot = sessionManager.GetAll().OfType<BotSession>().Single();
        firstBot.Name.Should().Be("ShadowTrader");

        // Open a second bot
        var gm2 = CreatePlayer(sessionManager, characterId: 1006, accountId: 2006, out _);
        gm2.IsGM = true;
        gm2.Inventory[InventoryConstants.InventoryStart].ItemId = ItemId;
        gm2.Inventory[InventoryConstants.InventoryStart].Count = 1;
        gm2.Trade.IsSellingMerchantPreparing = true;
        gm2.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 1,
            Price = 100,
            OriginalSlot = InventoryConstants.InventoryStart,
        };

        var packet2 = new Packet(GameOpcodes.GS_MERCHANT);
        packet2.WriteString("Stall 2");
        await lifecycleService.InsertAsync(gm2, packet2);

        var secondBot = sessionManager.GetAll().OfType<BotSession>().FirstOrDefault(b => b.Name == "BazarHero");
        secondBot.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveActiveBotsAsync_And_LoadAllBotsAsync_RestoresBots()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var botService = provider.GetRequiredService<IMerchantBotService>();
        var lifecycleService = provider.GetRequiredService<IMerchantLifecycleService>();

        // Create GM and open stall to spawn bot
        var gm = CreatePlayer(sessionManager, characterId: 1002, accountId: 2002, out _);
        gm.IsGM = true;
        gm.ZoneId = 21;
        gm.X = 350f;
        gm.Z = 450f;

        gm.Inventory[InventoryConstants.InventoryStart].ItemId = ItemId;
        gm.Inventory[InventoryConstants.InventoryStart].Count = 2;
        gm.Trade.IsSellingMerchantPreparing = true;
        gm.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 2,
            Price = 500,
            OriginalSlot = InventoryConstants.InventoryStart,
        };

        var insertPacket = new Packet(GameOpcodes.GS_MERCHANT);
        insertPacket.WriteString("SaveLoad Test Stall");
        await lifecycleService.InsertAsync(gm, insertPacket);

        sessionManager.GetAll().OfType<BotSession>().Should().HaveCount(1);

        // Save active bots
        var savedCount = await botService.SaveActiveBotsAsync();
        savedCount.Should().BeGreaterThanOrEqualTo(1);

        // Despawn all bots
        await botService.ClearAllBotsAsync();
        sessionManager.GetAll().OfType<BotSession>().Should().BeEmpty();

        // Load all bots back
        var loadedCount = await botService.LoadAllBotsAsync();
        loadedCount.Should().BeGreaterThanOrEqualTo(1);

        var loadedBots = sessionManager.GetAll().OfType<BotSession>().ToList();
        loadedBots.Should().NotBeEmpty();
        var loadedBot = loadedBots.First();
        loadedBot.Trade.IsMerchanting.Should().BeTrue();
        loadedBot.Trade.MerchantAdvert.Should().Be("SaveLoad Test Stall");
        loadedBot.Trade.MerchantItems[0]!.ItemId.Should().Be(ItemId);
        loadedBot.Trade.MerchantItems[0]!.Price.Should().Be(500);
    }

    [Fact]
    public async Task PlayerBuyingFromBot_TransfersItemAndDecrementsStock()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var lifecycleService = provider.GetRequiredService<IMerchantLifecycleService>();
        var merchantCoordinator = provider.GetRequiredService<IMerchantPacketCoordinator>();

        // GM creates merchant bot
        var gm = CreatePlayer(sessionManager, characterId: 1003, accountId: 2003, out _);
        gm.IsGM = true;
        gm.Inventory[InventoryConstants.InventoryStart].ItemId = ItemId;
        gm.Inventory[InventoryConstants.InventoryStart].Count = 10;
        gm.Trade.IsSellingMerchantPreparing = true;
        gm.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 5,
            Price = 100,
            OriginalSlot = InventoryConstants.InventoryStart,
        };

        var insertPacket = new Packet(GameOpcodes.GS_MERCHANT);
        insertPacket.WriteString("Bot Store");
        await lifecycleService.InsertAsync(gm, insertPacket);

        var bot = sessionManager.GetAll().OfType<BotSession>().Single();

        // Real buyer arrives with money
        var buyer = CreatePlayer(sessionManager, characterId: 2001, accountId: 3001, out _, money: 1000);
        buyer.Trade.MerchantTargetUserId = bot.CharacterId;

        // Buyer buys 2 units of ItemId from slot 0
        var buyPacket = new Packet(GameOpcodes.GS_MERCHANT);
        buyPacket.WriteByte((byte)MerchantSubOpcode.ItemBuy);
        buyPacket.WriteInt(ItemId);
        buyPacket.WriteUShort(2); // buy count
        buyPacket.WriteByte(0); // stall slot
        buyPacket.WriteByte(0); // dest bag slot (0 = first free)

        await merchantCoordinator.HandleAsync(buyer.Client, buyPacket);

        // Buyer money reduced by 2 * 100 = 200
        buyer.Money.Should().Be(800);

        // Buyer received 2 items
        var buyerItem = buyer.Inventory.Skip(InventoryConstants.InventoryStart)
            .FirstOrDefault(s => s.ItemId == ItemId);
        buyerItem.Should().NotBeNull();
        buyerItem!.Count.Should().Be(2);

        // Bot stock decreased from 5 to 3
        bot.Trade.MerchantItems[0]!.Count.Should().Be(3);
    }

    [Fact]
    public async Task AdminGmCommands_SaveAndClearAndLoad()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var lifecycleService = provider.GetRequiredService<IMerchantLifecycleService>();
        var adminCoordinator = provider.GetRequiredService<IAdminPacketCoordinator>();

        // Create GM
        var gm = CreatePlayer(sessionManager, characterId: 1004, accountId: 2004, out var gmSent);
        gm.IsGM = true;
        gm.Inventory[InventoryConstants.InventoryStart].ItemId = ItemId;
        gm.Inventory[InventoryConstants.InventoryStart].Count = 1;
        gm.Trade.IsSellingMerchantPreparing = true;
        gm.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = ItemId,
            Count = 1,
            Price = 50,
            OriginalSlot = InventoryConstants.InventoryStart,
        };

        var insertPacket = new Packet(GameOpcodes.GS_MERCHANT);
        insertPacket.WriteString("Admin Test Stall");
        await lifecycleService.InsertAsync(gm, insertPacket);
        sessionManager.GetAll().OfType<BotSession>().Should().HaveCount(1);

        // Test +savemerchantbots command
        await adminCoordinator.HandleGmCommandAsync(gm, "+savemerchantbots");

        // Test +clearmerchantbots command
        await adminCoordinator.HandleGmCommandAsync(gm, "+clearmerchantbots");

        sessionManager.GetAll().OfType<BotSession>().Should().BeEmpty();

        // Test +loadbotmerchant command
        await adminCoordinator.HandleGmCommandAsync(gm, "+loadbotmerchant");

        sessionManager.GetAll().OfType<BotSession>().Should().NotBeEmpty();
    }

    private ServiceProvider Provider() => CreateProvider(
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
