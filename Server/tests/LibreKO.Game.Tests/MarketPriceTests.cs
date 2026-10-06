using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MarketPriceTests : GameTestBase
{
    private const int Sword = 120010000;
    private const int Potion = 389010000;
    private const byte NoHistory = 0;
    private const byte History = 1;
    private const byte TooSoon = 11;
    private const byte NotPremium = 41;
    private const int DaysShown = 5;
    private const byte Moradon = 21;

    private sealed record Reply(byte Result, int ItemId, (long Avg, long Max, long Min)[] Days, int Trades, uint LastUpdate);

    private static ServiceProvider Provider(Action<AppDbContext>? seed = null) => CreateProvider(db => seed?.Invoke(db), gameData =>
    {
        gameData.GetItem(Sword).Returns(new ItemData { Num = Sword, Name = "Sword", Kind = 21, Slot = 1, Countable = 0, Duration = 1000, ReqLevelMax = 100 });
        gameData.GetItem(Potion).Returns(new ItemData { Num = Potion, Name = "Potion", Kind = 255, Slot = 15, Countable = 1, Duration = 1, ReqLevelMax = 100 });
    });

    private static (UserSession Session, List<Packet> Sent, IClient Client) Player(ServiceProvider provider, int id, string name, bool premium = true)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: id, accountId: id + 1000);
        session.Name = name;
        session.Level = 60;
        session.Nation = AccountNation.ElMorad;
        session.ZoneId = Moradon;
        session.X = 800;
        session.Z = 400;
        session.Hp = 100;
        session.MaxHp = 100;
        session.Money = 1_000_000;
        if (premium)
        {
            session.PremiumService = 1;
            session.PremiumExpiry = DateTime.UtcNow.AddDays(10);
        }
        sessions.Regions.AddToRegion(session);
        return (session, sent, client);
    }

    private static async Task<Reply> Ask(ServiceProvider provider, IClient client, List<Packet> sent, int itemId)
    {
        var request = new Packet(GameOpcodes.GS_MARKET_PRICE);
        request.WriteInt(itemId);
        request.ResetOffset();
        await provider.GetRequiredService<IMarketPriceService>().HandleAsync(client, request);

        var packet = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_MARKET_PRICE);
        packet.ResetOffset();
        var result = packet.ReadByte();
        if (result != History)
            return new Reply(result, 0, [], 0, 0);
        var id = packet.ReadInt();
        var days = Enumerable.Range(0, DaysShown).Select(_ => (packet.ReadLong(), packet.ReadLong(), packet.ReadLong())).ToArray();
        return new Reply(result, id, days, packet.ReadInt(), packet.ReadUInt());
    }

    private static MarketPriceDay Day(int itemId, int daysAgo, long min, long max, long total, long quantity, int trades) => new()
    {
        ItemId = itemId,
        Day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysAgo),
        MinPrice = min,
        MaxPrice = max,
        Total = total,
        Quantity = quantity,
        Trades = trades,
        LastTradeAt = DateTime.UtcNow.Date.AddDays(-daysAgo).AddHours(12),
    };

    [Fact]
    public async Task APurchaseFromASellingStallShowsInTheHistory()
    {
        using var provider = Provider();
        var (merchant, _, _) = Player(provider, 200, "Seller");
        var (buyer, _, _) = Player(provider, 300, "Buyer");
        var (_, sent, client) = Player(provider, 100, "Viewer");
        var stallSlot = (byte)InventoryConstants.SlotMax;
        merchant.Inventory[stallSlot].ItemId = Sword;
        merchant.Inventory[stallSlot].Count = 1;
        merchant.Inventory[stallSlot].Durability = 1000;
        merchant.Trade.MerchantState = MerchantMode.Selling;
        merchant.Trade.MerchantItems[0] = new MerchantItem { ItemId = Sword, Price = 5000, Count = 1, Durability = 1000, OriginalSlot = stallSlot };
        buyer.Trade.MerchantTargetUserId = merchant.CharacterId;

        var buy = new Packet(GameOpcodes.GS_MERCHANT);
        buy.WriteInt(Sword);
        buy.WriteShort(1);
        buy.WriteByte(0);
        buy.WriteByte(0);
        buy.ResetOffset();
        await provider.GetRequiredService<IMerchantListingService>().BuyItemAsync(buyer, buy);

        var reply = await Ask(provider, client, sent, Sword);
        reply.Result.Should().Be(History);
        reply.ItemId.Should().Be(Sword);
        reply.Days[0].Should().Be((5000L, 5000L, 5000L));
        reply.Trades.Should().Be(1);
    }

    [Fact]
    public async Task ASaleToABuyingStallShowsInTheHistory()
    {
        using var provider = Provider();
        var (merchant, _, _) = Player(provider, 200, "Wanting");
        var (seller, _, _) = Player(provider, 300, "Selling");
        var (_, sent, client) = Player(provider, 100, "Viewer");
        merchant.Trade.MerchantState = MerchantMode.Buying;
        merchant.Trade.BuyMerchantItems[0] = new MerchantItem { ItemId = Potion, Price = 70, Count = 10 };
        seller.Inventory[InventoryConstants.SlotMax].ItemId = Potion;
        seller.Inventory[InventoryConstants.SlotMax].Count = 4;
        seller.Inventory[InventoryConstants.SlotMax].Durability = 1;
        seller.Trade.MerchantTargetUserId = merchant.CharacterId;

        var sell = new Packet(GameOpcodes.GS_MERCHANT);
        sell.WriteByte(0);
        sell.WriteByte(0);
        sell.WriteShort(4);
        sell.ResetOffset();
        await provider.GetRequiredService<IMerchantBuyingService>().BuyAsync(seller, sell);

        var reply = await Ask(provider, client, sent, Potion);
        reply.Result.Should().Be(History);
        reply.Days[0].Should().Be((70L, 70L, 70L));
        reply.Trades.Should().Be(1);
    }

    [Fact]
    public async Task TheAverageIsWeightedByQuantity()
    {
        using var provider = Provider();
        var (buyer, _, _) = Player(provider, 200, "Buyer");
        var (seller, _, _) = Player(provider, 300, "Seller");
        var (_, sent, client) = Player(provider, 100, "Viewer");
        var prices = provider.GetRequiredService<IMarketPriceService>();

        await prices.RecordAsync(buyer, seller, Potion, 100, 1);
        await prices.RecordAsync(buyer, seller, Potion, 200, 3);

        var reply = await Ask(provider, client, sent, Potion);
        reply.Days[0].Should().Be((175L, 200L, 100L));
        reply.Trades.Should().Be(2);
    }

    [Fact]
    public async Task TheFiveLatestTradingDaysComeNewestFirst()
    {
        using var provider = Provider(db => db.MarketPriceDays.AddRange(
            Day(Sword, 10, 1, 1, 1, 1, 1),
            Day(Sword, 8, 2, 2, 2, 1, 1),
            Day(Sword, 5, 30, 50, 80, 2, 2),
            Day(Sword, 3, 40, 40, 40, 1, 1),
            Day(Sword, 2, 50, 50, 50, 1, 1),
            Day(Sword, 1, 60, 60, 60, 1, 1),
            Day(Sword, 0, 70, 90, 160, 2, 2)));
        var (_, sent, client) = Player(provider, 100, "Viewer");

        var reply = await Ask(provider, client, sent, Sword);

        reply.Days.Should().Equal((80L, 90L, 70L), (60L, 60L, 60L), (50L, 50L, 50L), (40L, 40L, 40L), (40L, 50L, 30L));
        reply.Trades.Should().Be(7);
        reply.LastUpdate.Should().Be((uint)new DateTimeOffset(DateTime.SpecifyKind(DateTime.UtcNow.Date.AddHours(12), DateTimeKind.Utc)).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task FewerTradingDaysArePaddedWithZeros()
    {
        using var provider = Provider(db => db.MarketPriceDays.AddRange(Day(Sword, 4, 10, 10, 10, 1, 1), Day(Sword, 1, 20, 20, 20, 1, 1)));
        var (_, sent, client) = Player(provider, 100, "Viewer");

        var reply = await Ask(provider, client, sent, Sword);

        reply.Days.Should().Equal((20L, 20L, 20L), (10L, 10L, 10L), (0L, 0L, 0L), (0L, 0L, 0L), (0L, 0L, 0L));
    }

    [Fact]
    public async Task AnItemNobodyTradedHasNoHistory()
    {
        using var provider = Provider();
        var (_, sent, client) = Player(provider, 100, "Viewer");

        (await Ask(provider, client, sent, Sword)).Result.Should().Be(NoHistory);
    }

    [Fact]
    public async Task WithoutPremiumTheHistoryIsRefused()
    {
        using var provider = Provider(db => db.MarketPriceDays.Add(Day(Sword, 0, 10, 10, 10, 1, 1)));
        var (_, sent, client) = Player(provider, 100, "Viewer", premium: false);

        (await Ask(provider, client, sent, Sword)).Result.Should().Be(NotPremium);
    }

    [Fact]
    public async Task ASecondRequestWithinASecondIsRefused()
    {
        using var provider = Provider(db => db.MarketPriceDays.Add(Day(Sword, 0, 10, 10, 10, 1, 1)));
        var (_, sent, client) = Player(provider, 100, "Viewer");

        (await Ask(provider, client, sent, Sword)).Result.Should().Be(History);
        (await Ask(provider, client, sent, Sword)).Result.Should().Be(TooSoon);
    }

    [Fact]
    public async Task TradesWithABotStallAreNotCounted()
    {
        using var provider = Provider();
        var (buyer, _, _) = Player(provider, 200, "Buyer");
        var (_, sent, client) = Player(provider, 100, "Viewer");
        var bot = new BotSession(Substitute.For<IClient>(), 900, 1900);

        await provider.GetRequiredService<IMarketPriceService>().RecordAsync(buyer, bot, Potion, 100, 1);

        (await Ask(provider, client, sent, Potion)).Result.Should().Be(NoHistory);
    }

    [Fact]
    public async Task DaysPastTheRetentionArePruned()
    {
        using var provider = Provider(db => db.MarketPriceDays.AddRange(
            Day(Sword, MarketPriceService.RetentionDays + 1, 10, 10, 10, 1, 1),
            Day(Sword, MarketPriceService.RetentionDays - 1, 20, 20, 20, 1, 1)));

        await provider.GetRequiredService<IMarketPriceService>().PruneAsync(DateOnly.FromDateTime(DateTime.UtcNow));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.MarketPriceDays.Select(d => d.MinPrice).Should().Equal(20L);
    }
}
