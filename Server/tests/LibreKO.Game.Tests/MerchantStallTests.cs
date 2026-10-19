using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

// A stall sale is checked and made under both players' locks, sells only what the seller's
// slot still holds, and a buying stall pays out of gold it took when it opened.
public class MerchantStallTests : GameTestBase
{
    private const int Ware = 379_080_000;
    private const int Other = 379_090_000;
    private const int Price = 1_000;
    private const int StartingGold = 10_000;
    private const byte StallSlot = 0;
    private const byte BagPos = 0;

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

        // The item is looked up once before the stock is checked and once more before it is written.
        market.HoldEachRequestAtLookup(2);
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

        // The item is looked up twice while the sale is checked and once more before it is written.
        market.HoldEachRequestAtLookup(3);
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
    public async Task ASaveWhileABuyingStallIsOpenKeepsTheGoldItHolds()
    {
        using var market = new Market();
        var owner = market.Player(96014, gold: StartingGold);
        await market.OpenBuyingStallAsync(owner, (Ware, 5, Price));

        var character = new Character { Name = owner.Name };
        market.Provider.GetRequiredService<IUserSessionCharacterMapper>().ApplyToCharacter(owner, character);

        character.Money.Should().Be(StartingGold);
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
        private readonly CountdownEvent rendezvous = new(2);
        private readonly AsyncLocal<int> lookups = new();
        private volatile int holdAt;

        public ServiceProvider Provider { get; }

        public Market()
        {
            var ware = new ItemData { Num = Ware, Countable = 1, Weight = 1, Race = 1 };
            Provider = CreateProvider(_ => { }, gameData =>
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

        // From now on each request waits at its n-th item lookup until the other request gets
        // there too (or a second passes), so two players' requests reach the stall together:
        // both past their checks before either one writes, unless a lock keeps them apart.
        public void HoldEachRequestAtLookup(int lookup) => holdAt = lookup;

        private ItemData Hold(ItemData data)
        {
            if (holdAt > 0 && ++lookups.Value == holdAt && rendezvous.CurrentCount > 0)
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

        public Task ListAsync(UserSession seller, int itemId, byte bagPos) => SendAsync(seller, MerchantSubOpcode.ItemAdd, packet =>
        {
            packet.WriteInt(itemId);
            packet.WriteUShort(1);
            packet.WriteInt(Price);
            packet.WriteByte(bagPos);
            packet.WriteByte(StallSlot);
        });

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
            rendezvous.Dispose();
        }
    }
}
