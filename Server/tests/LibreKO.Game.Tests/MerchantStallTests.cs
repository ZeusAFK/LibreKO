using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

// Stall sales run under both players' locks and sell only what the seller's slot holds.
public class MerchantStallTests : GameTestBase
{
    private const int Ware = 379_080_000;
    private const int Other = 379_090_000;
    private const int Price = 1_000;
    private const int StartingGold = 10_000;
    private const byte StallSlot = 0;
    private const byte BagPos = 0;
    private const byte OtherBagPos = 1;
    private const long RentalEnds = 4_102_444_800;
    private const int RingSerial = 4242;
    private const float FarAway = 500f;
    private const int SomeoneElse = 1;
    private const ushort OtherRoom = 3;

    [Fact]
    public async Task ABuyFromASlotThatNoLongerHoldsTheListedItemSellsNothing()
    {
        using var market = new Market();
        var seller = market.Player(96001, gold: 0);
        ListByHand(seller, Ware, count: 1);
        // The listed item left the slot by some route; something else sits there now.
        Stock(Bag(seller, BagPos), Other, 1);
        var buyer = market.Player(96002, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;

        await market.BuyAsync(buyer, Ware, 1);

        CountIn(buyer, Ware).Should().Be(0, "a slot that no longer holds the listed item sells nothing");
        buyer.Money.Should().Be(StartingGold);
        seller.Money.Should().Be(0);
        Bag(seller, BagPos).ItemId.Should().Be(Other);
    }

    [Fact]
    public async Task ABuyFromASlotHoldingAnotherSealedCharacterSellsNothing()
    {
        using var market = new Market();
        var seller = market.Player(96003, gold: 0);
        var ring = Bag(seller, BagPos);
        Stock(ring, InventoryConstants.CypherRing, 1);
        ring.UniqueId = 501;
        await market.ListAsync(seller, InventoryConstants.CypherRing, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        ring.UniqueId = 777;
        var buyer = market.Player(96004, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;

        await market.BuyAsync(buyer, InventoryConstants.CypherRing, 1);

        CountIn(buyer, InventoryConstants.CypherRing).Should().Be(0, "the slot holds a different character than the one listed");
        buyer.Money.Should().Be(StartingGold);
        ring.UniqueId.Should().Be(777);
    }

    [Theory]
    [InlineData(BusyAs.Trading)]
    [InlineData(BusyAs.Merchanting)]
    [InlineData(BusyAs.Mining)]
    [InlineData(BusyAs.Dead)]
    public async Task ABuyerWhoIsBusyBuysNothing(BusyAs busy)
    {
        using var market = new Market();
        var seller = market.Player(96041, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        var buyer = market.Player(96042, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;
        switch (busy)
        {
            case BusyAs.Trading: buyer.Trade.ExchangeUser = SomeoneElse; break;
            case BusyAs.Merchanting: buyer.Trade.IsSellingMerchantPreparing = true; break;
            case BusyAs.Mining: buyer.IsMining = true; break;
            case BusyAs.Dead: buyer.Hp = 0; break;
        }

        await market.BuyAsync(buyer, Ware, 1);

        CountIn(buyer, Ware).Should().Be(0, "a busy buyer cannot also buy from a stall");
        buyer.Money.Should().Be(StartingGold);
    }

    public enum BusyAs { Trading, Merchanting, Mining, Dead }

    [Fact]
    public async Task ASellerWhoIsGatheringCannotSellIntoABuyingStall()
    {
        using var market = new Market();
        var owner = market.Player(96061, gold: Price);
        await market.OpenBuyingStallAsync(owner, (Ware, 1, Price));
        var seller = market.Player(96062, gold: 0);
        seller.Trade.MerchantTargetUserId = owner.CharacterId;
        Stock(Bag(seller, BagPos), Ware, 1);
        seller.IsMining = true;

        await market.SellAsync(seller, BagPos, 1);

        CountIn(seller, Ware).Should().Be(1);
        seller.Money.Should().Be(0);
    }

    [Fact]
    public async Task ABuyerInAnotherRoomBuysNothing()
    {
        using var market = new Market();
        var seller = market.Player(96059, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        var buyer = market.Player(96060, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;
        buyer.Room = OtherRoom;

        await market.BuyAsync(buyer, Ware, 1);

        CountIn(buyer, Ware).Should().Be(0, "a stall in one instance room does not sell into another");
    }

    [Fact]
    public async Task ABuyerAwayFromTheStallBuysNothing()
    {
        using var market = new Market();
        var seller = market.Player(96043, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        var buyer = market.Player(96044, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;
        buyer.X = FarAway;

        await market.BuyAsync(buyer, Ware, 1);

        CountIn(buyer, Ware).Should().Be(0, "a stall sells only to someone standing at it");
        buyer.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AListingThatPointsPastTheBagSellsNothing()
    {
        using var market = new Market();
        var seller = market.Player(96045, gold: 0);
        ListByHand(seller, Ware, count: 1);
        seller.Trade.MerchantItems[StallSlot].OriginalSlot = byte.MaxValue;
        var buyer = market.Player(96046, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;

        await market.BuyAsync(buyer, Ware, 1);

        CountIn(buyer, Ware).Should().Be(0);
        buyer.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AnItemThatCanNoLongerBeTradedSellsNothing()
    {
        using var market = new Market();
        var seller = market.Player(96047, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        Bag(seller, BagPos).Flag = (byte)ItemFlag.Bound;
        seller.Trade.MerchantItems[StallSlot].Flag = (byte)ItemFlag.Bound;
        var buyer = market.Player(96048, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;

        await market.BuyAsync(buyer, Ware, 1);

        CountIn(buyer, Ware).Should().Be(0, "a bound item is not sold even if the listing says bound too");
        Bag(seller, BagPos).Count.Should().Be(1);
    }

    [Fact]
    public async Task AnItemWhoseExpiryChangedSinceItWasListedSellsNothing()
    {
        using var market = new Market();
        var seller = market.Player(96049, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        Bag(seller, BagPos).ExpiresAt = RentalEnds;
        var buyer = market.Player(96050, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;

        await market.BuyAsync(buyer, Ware, 1);

        CountIn(buyer, Ware).Should().Be(0, "a permanent item was listed and a timed one sits in the slot now");
    }

    [Fact]
    public async Task AnItemWhoseFlagChangedSinceItWasListedSellsNothing()
    {
        using var market = new Market();
        var seller = market.Player(96051, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        Bag(seller, BagPos).Flag = (byte)ItemFlag.SealedPremium;
        var buyer = market.Player(96052, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;

        await market.BuyAsync(buyer, Ware, 1);

        CountIn(buyer, Ware).Should().Be(0, "the slot holds the item under another flag than the one listed");
    }

    [Fact]
    public async Task AnOpenStallsListingsDoNotChange()
    {
        using var market = new Market();
        var seller = market.Player(96060, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;

        await market.SendAsync(seller, MerchantSubOpcode.ItemCancel, packet => packet.WriteByte(StallSlot));
        var keptAfterCancel = seller.Trade.MerchantItems[StallSlot]!.ItemId;
        await market.ListAsync(seller, Ware, BagPos);

        keptAfterCancel.Should().Be(Ware, "a buyer pays the price they saw, so an open stall cannot be re-listed");
        seller.Trade.MerchantItems[StallSlot]!.Price.Should().Be(Price);
    }

    [Fact]
    public async Task AStallsGoodsAreListedOnlyToSomeoneAtTheStall()
    {
        using var market = new Market();
        var seller = market.Player(96069, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        var browser = market.Player(96070, gold: 0);
        browser.X = seller.X + FarAway;
        var sent = new List<Packet>();
        browser.Client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await market.SendAsync(browser, MerchantSubOpcode.ItemList, packet => packet.WriteInt(seller.CharacterId));

        sent.Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_MERCHANT, "the stall list is no server-wide price radar");
        browser.Trade.MerchantTargetUserId.Should().Be(-1);
    }

    [Fact]
    public async Task ABuyerWhoLookedBeforeTheStallClosedBuysNothingFromItsReopening()
    {
        using var market = new Market();
        var seller = market.Player(96063, gold: 0);
        Stock(Bag(seller, BagPos), Ware, 1);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        var buyer = market.Player(96064, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;
        seller.Trade.MerchantViewerId = buyer.CharacterId;

        await market.SendAsync(seller, MerchantSubOpcode.Close);
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        await market.BuyAsync(buyer, Ware, 1);

        buyer.Money.Should().Be(StartingGold, "the buyer saw the earlier stall and its prices");
        CountIn(buyer, Ware).Should().Be(0);
        seller.Trade.MerchantViewerId.Should().Be(-1);
    }

    [Fact]
    public async Task ASellerWhoLookedBeforeABuyingStallClosedSellsNothingToItsReopening()
    {
        using var market = new Market();
        var owner = market.Player(96065, gold: Price);
        await market.OpenBuyingStallAsync(owner, (Ware, 1, Price));
        var seller = market.Player(96066, gold: 0);
        seller.Trade.MerchantTargetUserId = owner.CharacterId;
        Stock(Bag(seller, BagPos), Ware, 1);

        await market.SendAsync(owner, MerchantSubOpcode.BuyClose);
        await market.OpenBuyingStallAsync(owner, (Ware, 1, Price));
        await market.SellAsync(seller, BagPos, 1);

        seller.Money.Should().Be(0, "the seller saw the earlier stall and its prices");
        CountIn(seller, Ware).Should().Be(1);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(70, false)]
    [InlineData(71, false)]
    [InlineData(73, false)]
    public async Task ATimedOrTradeLockedItemIsNotListed(byte race, bool timed)
    {
        using var market = new Market(race: race);
        var seller = market.Player(96061, gold: 0);
        var ware = Bag(seller, BagPos);
        Stock(ware, Ware, 1);
        if (timed)
            ware.ExpiresAt = RentalEnds;

        await market.ListAsync(seller, Ware, BagPos);

        seller.Trade.MerchantItems[StallSlot].Should().Match<MerchantItem?>(item => item == null || item.IsEmpty,
            "the client never lists it, so the server takes no such listing");
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(70, false)]
    [InlineData(73, false)]
    public async Task ATimedOrTradeLockedItemIsNotSoldToABuyingStall(byte race, bool timed)
    {
        using var market = new Market(race: race);
        var owner = market.Player(96067, gold: Price);
        await market.OpenBuyingStallAsync(owner, (Ware, 1, Price));
        var seller = market.Player(96068, gold: 0);
        seller.Trade.MerchantTargetUserId = owner.CharacterId;
        Stock(Bag(seller, BagPos), Ware, 1);
        if (timed)
            Bag(seller, BagPos).ExpiresAt = RentalEnds;

        await market.SellAsync(seller, BagPos, 1);

        seller.Money.Should().Be(0);
        CountIn(seller, Ware).Should().Be(1);
    }

    [Fact]
    public async Task AListedItemIsBoughtWithItsFlag()
    {
        using var market = new Market();
        var seller = market.Player(96053, gold: 0);
        var ware = Bag(seller, BagPos);
        Stock(ware, Ware, 1);
        ware.Flag = (byte)ItemFlag.NotBound;
        await market.ListAsync(seller, Ware, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        var buyer = market.Player(96054, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;

        await market.BuyAsync(buyer, Ware, 1);

        var bought = buyer.Inventory.Skip(InventoryConstants.InventoryStart).Single(slot => slot.ItemId == Ware);
        bought.Flag.Should().Be((byte)ItemFlag.NotBound);
    }

    [Fact]
    public async Task AListedSealedCharacterIsBoughtWithItsSerial()
    {
        using var market = new Market();
        var seller = market.Player(96055, gold: 0);
        var ring = Bag(seller, BagPos);
        Stock(ring, InventoryConstants.CypherRing, 1);
        ring.UniqueId = RingSerial;
        await market.ListAsync(seller, InventoryConstants.CypherRing, BagPos);
        seller.Trade.MerchantState = MerchantMode.Selling;
        var buyer = market.Player(96056, gold: StartingGold);
        buyer.Trade.MerchantTargetUserId = seller.CharacterId;

        await market.BuyAsync(buyer, InventoryConstants.CypherRing, 1);

        buyer.Inventory.Single(slot => slot.ItemId == InventoryConstants.CypherRing).UniqueId.Should().Be(RingSerial);
    }

    [Fact]
    public async Task TwoBuyersCannotBothTakeTheLastItemOfAStack()
    {
        using var market = new Market();
        var seller = market.Player(96005, gold: 0);
        ListByHand(seller, Ware, count: 1);
        Stock(Bag(seller, BagPos), Ware, 1);
        var first = market.Player(96006, gold: StartingGold);
        var second = market.Player(96007, gold: StartingGold);
        first.Trade.MerchantTargetUserId = seller.CharacterId;
        second.Trade.MerchantTargetUserId = seller.CharacterId;

        // The item is looked up before the locks and again inside them.
        market.HoldEachRequestAtLookups(2);
        await Task.WhenAll(
            Task.Run(() => market.BuyAsync(first, Ware, 1)),
            Task.Run(() => market.BuyAsync(second, Ware, 1)));

        (CountIn(first, Ware) + CountIn(second, Ware)).Should().Be(1, "one item can be sold once");
        (first.Money + second.Money).Should().Be(2 * StartingGold - Price);
        seller.Money.Should().Be(Price);
    }

    [Fact]
    public async Task TwoSellersCannotBothBePaidForTheLastWantedItem()
    {
        using var market = new Market();
        var owner = market.Player(96008, gold: Price);
        await market.OpenBuyingStallAsync(owner, (Ware, 1, Price));
        var first = market.Player(96009, gold: 0);
        var second = market.Player(96010, gold: 0);
        foreach (var seller in new[] { first, second })
        {
            seller.Trade.MerchantTargetUserId = owner.CharacterId;
            Stock(Bag(seller, BagPos), Ware, 1);
        }

        // Lookups 1-2 are outside the locks, 3-4 inside, and 5 right before the write.
        market.HoldEachRequestAtLookups(3, 5);
        await Task.WhenAll(
            Task.Run(() => market.SellAsync(first, BagPos, 1)),
            Task.Run(() => market.SellAsync(second, BagPos, 1)));

        (first.Money + second.Money).Should().Be(Price, "the stall wanted one item and held gold for one");
        (CountIn(first, Ware) + CountIn(second, Ware)).Should().Be(1);
        CountIn(owner, Ware).Should().Be(1);
        owner.Money.Should().BeGreaterThanOrEqualTo(0, "a stall can never pay out gold its owner does not have");
    }

    [Fact]
    public async Task ABuyingStallTakesItsGoldWhenItOpensAndGivesBackWhatIsLeftWhenItCloses()
    {
        using var market = new Market();
        var owner = market.Player(96011, gold: StartingGold);

        await market.OpenBuyingStallAsync(owner, (Ware, 5, Price));

        owner.Money.Should().Be(StartingGold - 5 * Price, "the stall holds the gold for its whole list");

        var seller = market.Player(96012, gold: 0);
        seller.Trade.MerchantTargetUserId = owner.CharacterId;
        Stock(Bag(seller, BagPos), Ware, 2);
        await market.SellAsync(seller, BagPos, 2);

        seller.Money.Should().Be(2 * Price);
        owner.Money.Should().Be(StartingGold - 5 * Price, "the sale is paid from the stall's gold");

        await market.SendAsync(owner, MerchantSubOpcode.BuyClose);

        owner.Money.Should().Be(StartingGold - 2 * Price);
        owner.Trade.IsMerchanting.Should().BeFalse();
    }

    [Fact]
    public async Task AMerchantKilledByAMonsterGetsTheStallsGoldBackAndIsNoLongerBusy()
    {
        using var market = new Market();
        var owner = market.Player(96013, gold: StartingGold);
        await market.OpenBuyingStallAsync(owner, (Ware, 5, Price));

        owner.Hp = 0;
        await market.Provider.GetRequiredService<INpcAiDeathService>()
            .HandlePlayerKilledByNpcAsync(owner, new NpcInstance { NpcType = 12 });
        owner.Hp = 100;

        owner.Money.Should().Be(StartingGold);
        owner.Trade.IsMerchanting.Should().BeFalse();
        owner.Trade.IsMerchantPreparing.Should().BeFalse("a closed stall must not keep its owner busy after reviving");
    }

    [Fact]
    public async Task GoldAClosingStallCannotFitInThePurseGoesToTheWarehouse()
    {
        using var market = new Market();
        var owner = market.Player(96015, gold: StartingGold);
        await market.OpenBuyingStallAsync(owner, (Ware, 5, Price));
        // Gold reached the purse by some route while the stall held its share.
        owner.Money = ExchangePacketConstants.CoinMax - 1_000;

        await market.SendAsync(owner, MerchantSubOpcode.BuyClose);

        owner.Money.Should().Be(ExchangePacketConstants.CoinMax, "the purse never goes past the cap");
        owner.WarehouseMoney.Should().Be(5 * Price - 1_000, "the gold that no longer fits is kept, not destroyed");
        owner.Client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .Should().Contain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_CHAT,
                "the owner is told where the gold went");
    }

    [Fact]
    public async Task ASaveWithMoreGoldThanThePurseHoldsKeepsTheRestInTheWarehouse()
    {
        using var market = new Market();
        var owner = market.Player(96016, gold: StartingGold);
        await market.OpenBuyingStallAsync(owner, (Ware, 5, Price));
        owner.Money = ExchangePacketConstants.CoinMax - 1_000;

        var mapper = market.Provider.GetRequiredService<IUserSessionCharacterMapper>();
        var character = new Character { Name = owner.Name };
        var warehouse = new Warehouse();
        mapper.ApplyToCharacter(owner, character);
        mapper.ApplyToWarehouse(owner, warehouse);

        character.Money.Should().Be(ExchangePacketConstants.CoinMax);
        warehouse.Money.Should().Be(5 * Price - 1_000);
    }

    [Fact]
    public async Task ASaveWhileABuyingStallIsOpenKeepsTheGoldItHolds()
    {
        using var market = new Market();
        var owner = market.Player(96014, gold: StartingGold);
        await market.OpenBuyingStallAsync(owner, (Ware, 5, Price));

        var character = new Character { Name = owner.Name };
        market.Provider.GetRequiredService<IUserSessionCharacterMapper>().ApplyToCharacter(owner, character);

        character.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task ASaveNeverCatchesAChangeToThePlayerHalfWay()
    {
        const int ownerId = 96017;
        using var market = new Market(db => db.Characters.Add(new Character
        {
            Id = ownerId, AccountId = ownerId + 1000, Name = "Trader96017", Race = 1, Class = 101, Face = 1, Hair = 1,
            Level = 80, Hp = 100, Mp = 100, MapId = 21,
            Items = new byte[InventoryConstants.InventoryTotal * 8], SkillPointData = new byte[9],
        }));
        var owner = market.Player(ownerId, gold: StartingGold);
        var persister = market.Provider.GetRequiredService<ICharacterStatePersister>();
        // A first save warms the database up, so the one below reaches the player's lock at once.
        await persister.SaveAsync(owner);
        using var halfWay = new ManualResetEventSlim();
        using var saved = new ManualResetEventSlim();

        // Another connection pays, takes the item and hurts the player in one locked step.
        var purchase = Task.Run(() => owner.WithLock(s =>
        {
            s.Money -= Price;
            halfWay.Set();
            saved.Wait(TimeSpan.FromSeconds(2));
            Stock(Bag(s, BagPos), Ware, 1);
            s.Hp = 40;
        }));
        halfWay.Wait();
        await persister.SaveAsync(owner);
        saved.Set();
        await purchase;

        await using var scope = market.Provider.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.SingleAsync(c => c.Id == ownerId);
        row.Money.Should().Be(StartingGold - Price);
        row.Items.Should().Equal(owner.SerializeItems(), "the gold and the item it paid for are saved together");
        row.Hp.Should().Be(40, "every field comes from the same moment");
    }

    [Fact]
    public async Task LeavingTheZoneClosesAStallAndHandsBackItsGold()
    {
        using var market = new Market();
        var owner = market.Player(96018, gold: StartingGold);
        await market.OpenBuyingStallAsync(owner, (Ware, 5, Price));

        await market.Provider.GetRequiredService<IZoneTransitionService>().ChangeZoneAsync(owner, 1, 100f, 100f);

        owner.Trade.IsMerchanting.Should().BeFalse();
        owner.Trade.IsMerchantPreparing.Should().BeFalse("a stall left behind must not keep its owner busy");
        owner.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task DyingToAnotherPlayerClosesAStallAndHandsBackItsGold()
    {
        using var market = new Market();
        var owner = market.Player(96019, gold: StartingGold);
        var killer = market.Player(96020, gold: 0);
        await market.OpenBuyingStallAsync(owner, (Ware, 5, Price));

        owner.Hp = 0;
        await market.Provider.GetRequiredService<ICombatLifecycleService>().HandlePlayerDeathAsync(owner, killer);

        owner.Trade.IsMerchanting.Should().BeFalse();
        owner.Trade.IsMerchantPreparing.Should().BeFalse("a stall must not keep its owner busy after reviving");
        owner.Money.Should().Be(StartingGold);
    }

    private static void ListByHand(UserSession seller, int itemId, ushort count)
    {
        seller.Trade.MerchantState = MerchantMode.Selling;
        seller.Trade.MerchantItems[StallSlot] = new MerchantItem
        {
            ItemId = itemId, Count = count, Price = Price,
            OriginalSlot = InventoryConstants.InventoryStart + BagPos,
        };
    }

    private static void Stock(ItemSlot slot, int itemId, ushort count)
    {
        slot.ItemId = itemId;
        slot.Count = count;
    }

    private static ItemSlot Bag(UserSession session, byte pos) =>
        session.Inventory[InventoryConstants.InventoryStart + pos];

    private static int CountIn(UserSession session, int itemId) =>
        session.Inventory.Skip(InventoryConstants.InventoryStart).Take(InventoryConstants.HaveMax)
            .Where(slot => slot.ItemId == itemId).Sum(slot => slot.Count);

    private sealed class Market : IDisposable
    {
        private readonly Dictionary<int, CountdownEvent> holds = [];
        private readonly AsyncLocal<int> lookups = new();

        public ServiceProvider Provider { get; }

        public Market(Action<AppDbContext>? seed = null, byte race = 1)
        {
            var ware = new ItemData { Num = Ware, Countable = 1, Weight = 1, Race = race };
            Provider = CreateProvider(db => seed?.Invoke(db), gameData =>
            {
                gameData.GetItem(Ware).Returns(_ => Hold(ware));
                gameData.GetItem(Other).Returns(new ItemData { Num = Other, Countable = 1, Weight = 1, Race = 1 });
                gameData.GetItem(InventoryConstants.CypherRing).Returns(new ItemData
                {
                    Num = InventoryConstants.CypherRing, Countable = 0, Weight = 1, Race = 1,
                });
                gameData.GetMaxExpForLevel(Arg.Any<byte>()).Returns(1000L);
                gameData.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>());
            });
        }

        // Each request now waits at these lookups until the other arrives or a second passes.
        public void HoldEachRequestAtLookups(params int[] points)
        {
            foreach (var point in points)
                holds[point] = new CountdownEvent(2);
        }

        private ItemData Hold(ItemData data)
        {
            if (holds.Count > 0 && holds.TryGetValue(++lookups.Value, out var rendezvous) && rendezvous.CurrentCount > 0)
            {
                rendezvous.Signal();
                rendezvous.Wait(TimeSpan.FromSeconds(1));
            }

            return data;
        }

        public UserSession Player(int characterId, int gold)
        {
            var client = Substitute.For<IClient>();
            client.Id.Returns(Guid.NewGuid());
            client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

            var sessionManager = Provider.GetRequiredService<SessionManager>();
            var session = sessionManager.CreateSession(client, characterId, characterId + 1000);
            session.Name = $"Trader{characterId}";
            session.ZoneId = 21;
            session.Level = 80;
            session.Hp = 100;
            session.Money = gold;
            session.Stats = new DerivedStats { MaxWeight = 10_000 };
            sessionManager.Regions.AddToRegion(session);
            return session;
        }

        public Task ListAsync(UserSession seller, int itemId, byte bagPos)
        {
            seller.Trade.IsSellingMerchantPreparing = true;
            return SendAsync(seller, MerchantSubOpcode.ItemAdd, packet =>
            {
                packet.WriteInt(itemId);
                packet.WriteUShort(1);
                packet.WriteInt(Price);
                packet.WriteByte(bagPos);
                packet.WriteByte(StallSlot);
            });
        }

        public Task BuyAsync(UserSession buyer, int itemId, ushort count) => SendAsync(buyer, MerchantSubOpcode.ItemBuy, packet =>
        {
            packet.WriteInt(itemId);
            packet.WriteUShort(count);
            packet.WriteByte(StallSlot);
            packet.WriteByte(0);
        });

        public async Task OpenBuyingStallAsync(UserSession owner, params (int Item, ushort Count, int Price)[] wanted)
        {
            owner.Trade.IsBuyingMerchantPreparing = true;
            await SendAsync(owner, MerchantSubOpcode.BuyInsert, packet =>
            {
                packet.WriteByte((byte)wanted.Length);
                foreach (var (item, count, price) in wanted)
                {
                    packet.WriteInt(item);
                    packet.WriteUShort(count);
                    packet.WriteInt(price);
                }
            });
        }

        public Task SellAsync(UserSession seller, byte bagPos, ushort count) => SendAsync(seller, MerchantSubOpcode.BuyBuy, packet =>
        {
            packet.WriteByte(bagPos);
            packet.WriteByte(StallSlot);
            packet.WriteUShort(count);
        });

        public Task SendAsync(UserSession session, MerchantSubOpcode sub, Action<Packet>? body = null)
        {
            var packet = new Packet(GameOpcodes.GS_MERCHANT);
            packet.WriteByte((byte)sub);
            body?.Invoke(packet);
            packet.ResetOffset();
            return Provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(session.Client, packet);
        }

        public void Dispose()
        {
            Provider.Dispose();
            foreach (var rendezvous in holds.Values)
                rendezvous.Dispose();
        }
    }
}
