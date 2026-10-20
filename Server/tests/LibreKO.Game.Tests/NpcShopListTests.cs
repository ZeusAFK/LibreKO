using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class NpcShopListTests : GameTestBase
{
    private const byte Buy = 1;
    private const byte Sell = 2;
    private const int WeaponShop = 101001;
    private const int PotionShop = 253000;
    private const int RentalShop = 231000;
    private const int NoShop = 0;
    private const int Sword = 110150001;
    private const int Spear = 110450001;
    private const int Unique = 120110001;
    private const int RentalSword = 121470000;
    private const int Potion = 389010000;
    private const int SpearPrice = 500;
    private const int PotionSalePrice = 1;
    private const int StartingGold = 100_000;
    private const byte Zone = 21;
    private const byte OtherZone = Zone + 1;
    private const long RentalEnds = 4_102_444_800;
    private const ushort Held = 3;
    private const int SealedCharacter = 4242;

    [Fact]
    public async Task AListedItemBoughtFromItsOwnCellIsSold()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var npc = Npc(provider, WeaponShop);

        await TradeAsync(provider, client, npc, Buy, WeaponShop, Spear, line: 0, index: 1);

        Bag(session).ItemId.Should().Be(Spear);
        session.Money.Should().Be(StartingGold - SpearPrice);
    }

    [Fact]
    public async Task AMerchantDoesNotSellAnItemItsShopListLacks()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var npc = Npc(provider, WeaponShop);

        await TradeAsync(provider, client, npc, Buy, WeaponShop, Unique, line: 0, index: 1);

        Bag(session).IsEmpty.Should().BeTrue("the client can name any item, the shop list cannot be changed");
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AListedItemNamedAtAnotherItemsCellIsRefused()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var npc = Npc(provider, WeaponShop);

        await TradeAsync(provider, client, npc, Buy, WeaponShop, Spear, line: 0, index: 0);

        Bag(session).IsEmpty.Should().BeTrue("cell 0 holds the sword, not the spear");
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AListedItemNamedOnAnotherTabIsRefused()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var npc = Npc(provider, WeaponShop);

        await TradeAsync(provider, client, npc, Buy, WeaponShop, Spear, line: 1, index: 1);

        Bag(session).IsEmpty.Should().BeTrue("the spear stands on the first tab");
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AnotherShopsListDoesNotCount()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var potionSeller = Npc(provider, PotionShop);

        await TradeAsync(provider, client, potionSeller, Buy, PotionShop, Sword, line: 0, index: 0);

        Bag(session).IsEmpty.Should().BeTrue("the sword is on the weapon shop's list, not this one's");
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AnNpcThatOpensNoShopDoesNotSellItsGroupsList()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var rental = Npc(provider, RentalShop, NpcData.TypeRental);

        await TradeAsync(provider, client, rental, Buy, RentalShop, RentalSword, line: 0, index: 0);

        Bag(session).IsEmpty.Should().BeTrue("a rental NPC lends through its own window; its free list is not a shop");
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AMerchantWithoutAShopListStillBuysFromAPlayer()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var merchant = Npc(provider, NoShop);
        Stock(Bag(session), Potion, 5);

        await TradeAsync(provider, client, merchant, Sell, NoShop, Potion, line: 0, index: 0);

        Bag(session).Count.Should().Be(4);
        session.Money.Should().BeGreaterThan(StartingGold);
    }

    [Fact]
    public async Task AnNpcThatIsNoMerchantDoesNotBuyFromAPlayer()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var guard = Npc(provider, NoShop, NpcData.TypeGuard);
        Stock(Bag(session), Potion, 5);

        await TradeAsync(provider, client, guard, Sell, NoShop, Potion, line: 0, index: 0);

        Bag(session).Count.Should().Be(5);
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AMerchantInAnotherZoneIsOutOfReach()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var npc = Npc(provider, WeaponShop, zone: OtherZone);

        await TradeAsync(provider, client, npc, Buy, WeaponShop, Spear, line: 0, index: 1);

        Bag(session).IsEmpty.Should().BeTrue("standing on the same x and z of another zone is not standing at the merchant");
        session.Money.Should().Be(StartingGold);
    }

    [Theory]
    [InlineData(Zone, true)]
    [InlineData(OtherZone, false)]
    public async Task AnItemIsBoughtBackOnlyFromAnNpcInTheBuyersZone(byte npcZone, bool boughtBack)
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        GrandMerchant(provider, session, npcZone);
        var sold = new ItemSlot { ItemId = Spear, Count = 1, Durability = 100 };
        RepurchaseRules.Record(session.Repurchases, sold, 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte((byte)ItemTradeResult.Repurchase);
        packet.WriteByte(ItemTradePacketWriter.RepurchaseBuy);
        packet.WriteByte(0);
        packet.WriteInt(Spear);
        packet.WriteInt(ItemTradeService.RepurchaseNpcId);
        packet.ResetOffset();
        await provider.GetRequiredService<IItemTradeService>().HandleTradeAsync(client, packet);

        Bag(session).ItemId.Should().Be(boughtBack ? Spear : 0);
        session.Repurchases.Should().HaveCount(boughtBack ? 0 : 1);
    }

    [Fact]
    public async Task APotionBoughtOntoARentedStackIsRefused()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var npc = Npc(provider, PotionShop);
        Stock(Bag(session), Potion, Held);
        Bag(session).ExpiresAt = RentalEnds;
        Bag(session).Flag = (byte)ItemFlag.Rented;

        await TradeAsync(provider, client, npc, Buy, PotionShop, Potion, line: 0, index: 0);

        Bag(session).Count.Should().Be(Held, "a bought potion would become rented with the stack");
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task AnItemIsBoughtBackOnlyFromTheGrandMerchantSpokenTo()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var monster = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            NpcId = MonsterNpcId, IsMonster = true, ZoneId = Zone, X = 10, Z = 10, MaxHp = 1, Hp = 1,
        });
        session.Quest.EventNpcUniqueId = monster.UniqueId;
        var sold = new ItemSlot { ItemId = Spear, Count = 1, Durability = 100 };
        RepurchaseRules.Record(session.Repurchases, sold, 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte((byte)ItemTradeResult.Repurchase);
        packet.WriteByte(ItemTradePacketWriter.RepurchaseBuy);
        packet.WriteByte(0);
        packet.WriteInt(Spear);
        packet.WriteInt(ItemTradeService.RepurchaseNpcId);
        packet.ResetOffset();
        await provider.GetRequiredService<IItemTradeService>().HandleTradeAsync(client, packet);

        Bag(session).IsEmpty.Should().BeTrue("naming an NPC in range is not speaking to the Grand Merchant");
    }

    [Fact]
    public async Task ABoughtBackItemKeepsItsFlagAndDoesNotJoinAPlainStack()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        GrandMerchant(provider, session, Zone);
        Stock(Bag(session), Potion, Held);
        var sold = new ItemSlot { ItemId = Potion, Count = 2, Durability = 100, Flag = (byte)ItemFlag.NotBound };
        RepurchaseRules.Record(session.Repurchases, sold, 2, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte((byte)ItemTradeResult.Repurchase);
        packet.WriteByte(ItemTradePacketWriter.RepurchaseBuy);
        packet.WriteByte(0);
        packet.WriteInt(Potion);
        packet.WriteInt(ItemTradeService.RepurchaseNpcId);
        packet.ResetOffset();
        await provider.GetRequiredService<IItemTradeService>().HandleTradeAsync(client, packet);

        Bag(session).Count.Should().Be(Held);
        session.Inventory.Single(slot => slot.ItemId == Potion && slot.Flag == (byte)ItemFlag.NotBound).Count.Should().Be(2);
    }

    [Fact]
    public async Task ASealedCharacterIsNotSoldToAMerchant()
    {
        using var provider = Provider();
        var (session, client) = Shopper(provider);
        var merchant = Npc(provider, NoShop);
        Stock(Bag(session), InventoryConstants.CypherRing, 1);
        Bag(session).UniqueId = SealedCharacter;

        await TradeAsync(provider, client, merchant, Sell, NoShop, InventoryConstants.CypherRing, line: 0, index: 0);

        Bag(session).UniqueId.Should().Be(SealedCharacter, "a sold ring could never unseal its character again");
        session.Money.Should().Be(StartingGold);
    }

    private static ServiceProvider Provider() => CreateProvider(_ => { }, Stock);

    private static void Stock(IGameDataService gameData)
    {
        foreach (var (itemId, countable, price) in new[]
                 {
                     (Sword, 0, 400), (Spear, 0, SpearPrice), (Unique, 0, 100), (RentalSword, 0, 0), (Potion, 1, 50),
                     (InventoryConstants.CypherRing, 0, 100),
                 })
            gameData.GetItem(itemId).Returns(new ItemData
            {
                Num = itemId, Countable = (byte)countable, BuyPrice = price, SellPrice = PotionSalePrice, Weight = 1,
                Race = 1, Duration = 100,
            });

        gameData.ItemSellsByGroup.Returns(new[]
        {
            new ItemSellData { SellingGroup = WeaponShop, Line = 0, Index = 0, ItemId = Sword },
            new ItemSellData { SellingGroup = WeaponShop, Line = 0, Index = 1, ItemId = Spear },
            new ItemSellData { SellingGroup = PotionShop, Line = 0, Index = 0, ItemId = Potion },
            new ItemSellData { SellingGroup = RentalShop, Line = 0, Index = 0, ItemId = RentalSword },
        }.ToLookup(sold => sold.SellingGroup));
    }

    private static async Task TradeAsync(
        ServiceProvider provider, IClient client, NpcInstance npc, byte type, int sellingGroup, int itemId,
        byte line, byte index)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(type);
        packet.WriteInt(sellingGroup);
        packet.WriteInt(npc.UniqueId);
        packet.WriteByte(1);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteUShort(1);
        if (type == Buy)
        {
            packet.WriteByte(line);
            packet.WriteByte(index);
        }

        packet.ResetOffset();
        await provider.GetRequiredService<IItemTradeService>().HandleTradeAsync(client, packet);
    }

    private static (UserSession Session, IClient Client) Shopper(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId: 96001, accountId: 97001);
        session.Hp = 100;
        session.ZoneId = Zone;
        session.X = 10;
        session.Z = 10;
        session.Money = StartingGold;
        session.Stats = new DerivedStats { MaxWeight = 10_000 };
        return (session, client);
    }

    // The client sends the merchant's kind; the merchant it spoke to is the one that counts.
    private static void GrandMerchant(ServiceProvider provider, UserSession session, byte zone) =>
        session.Quest.EventNpcUniqueId = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            NpcId = ItemTradeService.RepurchaseNpcId, ZoneId = zone, X = 10, Z = 10, MaxHp = 1, Hp = 1, NpcType = NpcData.TypeTradeMerchant,
        }).UniqueId;

    private const int MonsterNpcId = 101;

    private static NpcInstance Npc(
        ServiceProvider provider, int sellingGroup, byte npcType = NpcData.TypeTradeMerchant, byte zone = Zone) =>
        provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 5000,
            SellingGroup = sellingGroup,
            ZoneId = zone,
            X = 10,
            Z = 10,
            MaxHp = 1,
            Hp = 1,
            NpcType = npcType,
        });

    private static void Stock(ItemSlot slot, int itemId, ushort count)
    {
        slot.ItemId = itemId;
        slot.Count = count;
    }

    private static ItemSlot Bag(UserSession session) => session.Inventory[InventoryConstants.InventoryStart];
}
