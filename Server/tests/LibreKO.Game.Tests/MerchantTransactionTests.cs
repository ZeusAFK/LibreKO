using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class MerchantTransactionTests : GameTestBase
{
    private const int StackableItem = 379080000;
    private const int CoinMax = 2_100_000_000;

    [Fact]
    public async Task BuyingStallCannotBeOpenedForMoreGoldThanTheOwnerCarries()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var owner = Player(sessionManager, 8401, 9401, money: 100, out var sent);
        owner.Trade.IsBuyingMerchantPreparing = true;

        await Insert(provider, owner, wanted: (StackableItem, 1, 500));

        owner.Trade.IsBuyingMerchant.Should().BeFalse("500 is more gold than the owner is carrying");
        owner.Trade.BuyMerchantItems[0].IsEmpty.Should().BeTrue();
        Reply(sent, MerchantSubOpcode.BuyInsert)
            .Should().Be((byte)BuyingMerchantResult.SellerFundsTooLow);
    }

    [Fact]
    public async Task BuyingStallCountsTheWholeStackNotJustTheUnitPrice()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var owner = Player(sessionManager, 8402, 9402, money: 900, out var sent);
        owner.Trade.IsBuyingMerchantPreparing = true;

        await Insert(provider, owner, wanted: (StackableItem, 10, 100));

        owner.Trade.IsBuyingMerchant.Should().BeFalse("ten at 100 each is 1,000, more than 900");
        Reply(sent, MerchantSubOpcode.BuyInsert)
            .Should().Be((byte)BuyingMerchantResult.SellerFundsTooLow);
    }

    [Fact]
    public async Task BuyingStallAddsUpEveryLineBeforeAccepting()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var owner = Player(sessionManager, 8403, 9403, money: 1_500, out var sent);
        owner.Trade.IsBuyingMerchantPreparing = true;

        await Insert(provider, owner,
            (StackableItem, 1, 900),
            (StackableItem, 1, 900));

        owner.Trade.IsBuyingMerchant.Should().BeFalse("each line is affordable but the two together are not");
        Reply(sent, MerchantSubOpcode.BuyInsert)
            .Should().Be((byte)BuyingMerchantResult.SellerFundsTooLow);
    }

    [Fact]
    public async Task BuyingStallOpensWhenTheOwnerCanCoverTheWholeList()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var owner = Player(sessionManager, 8404, 9404, money: 1_000, out var sent);
        owner.Trade.IsBuyingMerchantPreparing = true;

        await Insert(provider, owner, (StackableItem, 2, 500));

        owner.Trade.IsBuyingMerchant.Should().BeTrue();
        Reply(sent, MerchantSubOpcode.BuyInsert).Should().Be((byte)BuyingMerchantResult.Accepted);
    }

    [Fact]
    public async Task BuyingStallRefusesAPriceBeyondTheCoinCap()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var owner = Player(sessionManager, 8405, 9405, money: CoinMax, out var sent);
        owner.Trade.IsBuyingMerchantPreparing = true;

        await Insert(provider, owner, (StackableItem, 1, int.MaxValue));

        owner.Trade.IsBuyingMerchant.Should().BeFalse();
        Reply(sent, MerchantSubOpcode.BuyInsert)
            .Should().Be((byte)BuyingMerchantResult.WrongItemSetup);
    }

    [Fact]
    public async Task SaleIsRefusedWhenTheStallOwnerSpentTheGoldAfterOpening()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var owner = Player(sessionManager, 8406, 9406, money: 10_000, out _);
        owner.Trade.MerchantState = MerchantMode.Buying;
        owner.Trade.BuyMerchantItems[0] = new MerchantItem
        {
            ItemId = StackableItem, Count = 5, Price = 1_000,
        };
        owner.Money = 500;

        var seller = Player(sessionManager, 8407, 9407, money: 0, out var sellerSent);
        seller.Trade.MerchantTargetUserId = owner.CharacterId;
        seller.Inventory[InventoryConstants.SlotMax].ItemId = StackableItem;
        seller.Inventory[InventoryConstants.SlotMax].Count = 5;

        await Sell(provider, seller, sellerSlot: 0, wantedSlot: 0, count: 1);

        seller.Money.Should().Be(0, "the owner can no longer pay");
        owner.Money.Should().Be(500);
        seller.Inventory[InventoryConstants.SlotMax].Count.Should().Be(5);
        Reply(sellerSent, MerchantSubOpcode.BuyBuy)
            .Should().Be((byte)BuyingMerchantResult.BuyerFundsTooLow);
    }

    [Fact]
    public async Task SaleIsRefusedWhenItWouldPushTheSellerOverTheCoinCap()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var owner = Player(sessionManager, 8408, 9408, money: 1_000_000, out _);
        owner.Trade.MerchantState = MerchantMode.Buying;
        owner.Trade.BuyMerchantItems[0] = new MerchantItem
        {
            ItemId = StackableItem, Count = 5, Price = 1_000_000,
        };

        var seller = Player(sessionManager, 8409, 9409, money: CoinMax, out var sellerSent);
        seller.Trade.MerchantTargetUserId = owner.CharacterId;
        seller.Inventory[InventoryConstants.SlotMax].ItemId = StackableItem;
        seller.Inventory[InventoryConstants.SlotMax].Count = 5;

        await Sell(provider, seller, sellerSlot: 0, wantedSlot: 0, count: 1);

        seller.Money.Should().Be(CoinMax, "gold must never wrap past the cap");
        owner.Money.Should().Be(1_000_000);
        Reply(sellerSent, MerchantSubOpcode.BuyBuy)
            .Should().Be((byte)BuyingMerchantResult.OverMaxLimit);
    }

    [Fact]
    public async Task BuyingFromASellingStallIsRefusedWhenItWouldOverflowTheOwnersPurse()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchant = Player(sessionManager, 8410, 9410, money: CoinMax, out _);
        merchant.Trade.MerchantState = MerchantMode.Selling;
        merchant.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = StackableItem, Count = 1, Price = 1_000_000,
            OriginalSlot = InventoryConstants.SlotMax,
        };
        merchant.Inventory[InventoryConstants.SlotMax].ItemId = StackableItem;
        merchant.Inventory[InventoryConstants.SlotMax].Count = 1;

        var buyer = Player(sessionManager, 8411, 9411, money: 5_000_000, out _);
        buyer.Trade.MerchantTargetUserId = merchant.CharacterId;

        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)MerchantSubOpcode.ItemBuy);
        packet.WriteInt(StackableItem);
        packet.WriteUShort(1);
        packet.WriteByte(0);
        packet.WriteByte(0);
        await provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(buyer.Client, packet);

        buyer.Money.Should().Be(5_000_000, "the sale must not go through");
        merchant.Money.Should().Be(CoinMax, "the seller's purse must never wrap negative");
        merchant.Trade.MerchantItems[0].Count.Should().Be(1);
    }

    private ServiceProvider Provider() => CreateProvider(
        _ => { },
        gameData => gameData.GetItem(StackableItem).Returns(new ItemData
        {
            Num = StackableItem, Countable = 1, Duration = 0,
        }));

    private static UserSession Player(
        SessionManager sessionManager, int characterId, int accountId, int money, out List<Packet> sent)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var captured = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(captured.Add), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        sent = captured;

        var session = sessionManager.CreateSession(client, characterId, accountId);
        session.ZoneId = 21;
        session.Level = 80;
        session.Hp = 100;
        session.Money = money;
        sessionManager.Regions.AddToRegion(session);
        return session;
    }

    private static Task Insert(
        ServiceProvider provider, UserSession owner, params (int Item, int Count, int Price)[] wanted)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)MerchantSubOpcode.BuyInsert);
        packet.WriteByte((byte)wanted.Length);
        foreach (var (item, count, price) in wanted)
        {
            packet.WriteInt(item);
            packet.WriteUShort((ushort)count);
            packet.WriteInt(price);
        }
        return provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(owner.Client, packet);
    }

    private static Task Sell(
        ServiceProvider provider, UserSession seller, byte sellerSlot, byte wantedSlot, ushort count)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)MerchantSubOpcode.BuyBuy);
        packet.WriteByte(sellerSlot);
        packet.WriteByte(wantedSlot);
        packet.WriteUShort(count);
        return provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(seller.Client, packet);
    }

    private static byte? Reply(List<Packet> sent, MerchantSubOpcode sub)
    {
        foreach (var packet in sent)
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_MERCHANT) continue;
            packet.ResetOffset();
            if (packet.ReadByte() != (byte)sub) continue;
            return packet.ReadByte();
        }
        return null;
    }

    [Fact]
    public async Task BuyingFromASellingStallIsRefusedWhenTheBuyerHasNoFreeSlot()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchant = SellingStall(sessionManager, 8412, 9412, price: 1_000, stock: 1);
        var buyer = Player(sessionManager, 8413, 9413, money: 500_000, out var buyerSent);
        buyer.Trade.MerchantTargetUserId = merchant.CharacterId;
        FillBags(buyer, 700000000);

        await Buy(provider, buyer, count: 1);

        buyer.Money.Should().Be(500_000, "a refused purchase must not take gold");
        merchant.Money.Should().Be(0);
        merchant.Trade.MerchantItems[0].Count.Should().Be(1, "the goods stay on the stall");
        BuyRefused(buyerSent).Should().BeTrue("the buyer must be told, not left waiting");
    }

    [Fact]
    public async Task BuyingAStackableMergesIntoAPartialStackRatherThanNeedingAnEmptySlot()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var merchant = SellingStall(sessionManager, 8414, 9414, price: 1_000, stock: 3);
        var buyer = Player(sessionManager, 8415, 9415, money: 500_000, out _);
        buyer.Trade.MerchantTargetUserId = merchant.CharacterId;
        FillBags(buyer, 700000000);
        buyer.Inventory[InventoryConstants.SlotMax + 5].ItemId = StackableItem;
        buyer.Inventory[InventoryConstants.SlotMax + 5].Count = 2;

        await Buy(provider, buyer, count: 3);

        buyer.Inventory[InventoryConstants.SlotMax + 5].Count.Should().Be(5);
        buyer.Money.Should().Be(500_000 - 3_000);
        merchant.Money.Should().Be(3_000);
    }

    [Fact]
    public async Task SellingIntoABuyingStallIsRefusedWhenTheOwnersBagsAreFull()
    {
        using var provider = Provider();
        var sessionManager = provider.GetRequiredService<SessionManager>();

        var owner = Player(sessionManager, 8416, 9416, money: 1_000_000, out _);
        owner.Trade.MerchantState = MerchantMode.Buying;
        owner.Trade.BuyMerchantItems[0] = new MerchantItem
        {
            ItemId = StackableItem, Count = 5, Price = 1_000,
        };
        FillBags(owner, 700000000);

        var seller = Player(sessionManager, 8417, 9417, money: 0, out var sellerSent);
        seller.Trade.MerchantTargetUserId = owner.CharacterId;
        seller.Inventory[InventoryConstants.SlotMax].ItemId = StackableItem;
        seller.Inventory[InventoryConstants.SlotMax].Count = 5;

        await Sell(provider, seller, sellerSlot: 0, wantedSlot: 0, count: 1);

        seller.Money.Should().Be(0);
        owner.Money.Should().Be(1_000_000);
        seller.Inventory[InventoryConstants.SlotMax].Count.Should().Be(5);
        Reply(sellerSent, MerchantSubOpcode.BuyBuy)
            .Should().Be((byte)BuyingMerchantResult.InventoryFull);
    }

    private static void FillBags(UserSession session, int filler)
    {
        for (var i = 0; i < InventoryConstants.HaveMax; i++)
        {
            session.Inventory[InventoryConstants.SlotMax + i].ItemId = filler;
            session.Inventory[InventoryConstants.SlotMax + i].Count = 1;
        }
    }

    private static UserSession SellingStall(
        SessionManager sessionManager, int characterId, int accountId, int price, ushort stock)
    {
        var merchant = Player(sessionManager, characterId, accountId, money: 0, out _);
        merchant.Trade.MerchantState = MerchantMode.Selling;
        merchant.Trade.MerchantItems[0] = new MerchantItem
        {
            ItemId = StackableItem, Count = stock, Price = price,
            OriginalSlot = InventoryConstants.SlotMax,
        };
        merchant.Inventory[InventoryConstants.SlotMax].ItemId = StackableItem;
        merchant.Inventory[InventoryConstants.SlotMax].Count = stock;
        return merchant;
    }

    private static Task Buy(ServiceProvider provider, UserSession buyer, ushort count)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte((byte)MerchantSubOpcode.ItemBuy);
        packet.WriteInt(StackableItem);
        packet.WriteUShort(count);
        packet.WriteByte(0);
        packet.WriteByte(0);
        return provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(buyer.Client, packet);
    }

    private static bool BuyRefused(List<Packet> sent)
    {
        foreach (var packet in sent)
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_MERCHANT) continue;
            packet.ResetOffset();
            if (packet.ReadByte() != (byte)MerchantSubOpcode.ItemBuy) continue;
            if (packet.ReadShort() != (short)MerchantResult.Succeeded) return true;
        }
        return false;
    }
}
