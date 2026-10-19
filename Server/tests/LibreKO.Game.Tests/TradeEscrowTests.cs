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

// An offer leaves the bag for escrow and comes back, or goes across, as the same item.
public class TradeEscrowTests : GameTestBase
{
    private const int GiverId = 92001;
    private const int TakerId = 92002;
    private const int Potion = 389010000;
    private const int Sword = 120110001;
    private const int Pickup = 379006000;
    private const byte PotionPos = 4;
    private const byte SwordPos = 5;
    private const ushort PotionStack = 5;
    private const long SwordExpiry = 1_900_000_000;
    private const long OldPotionExpiry = 1_800_000_000;

    [Fact]
    public async Task AnOfferedStackLeavesTheBagInsteadOfAPlaceholder()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, _, giverClient, _) = OpenTrade(provider);
        StockPotions(giver);

        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, PotionStack));

        BagSlot(giver, PotionPos).IsEmpty.Should().BeTrue();
        BagSlot(giver, PotionPos).Count.Should().Be(0, "an offered stack must not stay behind as a count-0 placeholder");
        giver.Trade.ExchangeItemList.Should().ContainSingle(entry => entry.ItemId == Potion && entry.Count == PotionStack);
    }

    [Fact]
    public async Task CancelReturnsEachOfferToItsOwnSlotWithItsIdentity()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, _, giverClient, _) = OpenTrade(provider);
        StockPotions(giver);
        StockSword(giver);

        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, 3));
        await SendAsync(provider, giverClient, AddPacket(SwordPos, Sword, 1));

        BagSlot(giver, SwordPos).IsEmpty.Should().BeTrue("the offered sword waits in escrow, not in the bag");
        BagSlot(giver, PotionPos).Count.Should().Be(2);

        await SendAsync(provider, giverClient, CancelPacket());

        BagSlot(giver, PotionPos).ItemId.Should().Be(Potion);
        BagSlot(giver, PotionPos).Count.Should().Be(PotionStack);
        var sword = BagSlot(giver, SwordPos);
        sword.ItemId.Should().Be(Sword);
        sword.Flag.Should().Be((byte)ItemFlag.NotBound);
        sword.ExpiresAt.Should().Be(SwordExpiry);
        sword.Durability.Should().Be(77);
        giver.Trade.ExchangeItemList.Should().BeEmpty();
        giver.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task CancelDoesNotPourTheOfferIntoWhateverFilledItsSlot()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, _, giverClient, _) = OpenTrade(provider);
        StockPotions(giver);

        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, PotionStack));

        // A pickup lands in the slot the offer freed.
        var filled = BagSlot(giver, PotionPos);
        filled.ItemId = Pickup;
        filled.Count = 1;

        await SendAsync(provider, giverClient, CancelPacket());

        filled.ItemId.Should().Be(Pickup);
        filled.Count.Should().Be(1, "the cancelled offer must not be added onto another item's count");
        CountInBag(giver, Potion).Should().Be(PotionStack);
        CountInBag(giver, Pickup).Should().Be(1);
    }

    [Fact]
    public async Task RemovingTheOfferedSlotThenCancellingLeavesNoCountBehind()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, _, giverClient, _) = OpenTrade(provider);
        StockPotions(giver);

        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, PotionStack));
        await provider.GetRequiredService<IItemRemoveService>().HandleAsync(giverClient, RemovePacket(PotionPos, Potion));
        await SendAsync(provider, giverClient, CancelPacket());

        CountInBag(giver, Potion).Should().Be(PotionStack);
        giver.Inventory.Should().NotContain(slot => slot.IsEmpty && slot.Count > 0,
            "an empty slot holding a count is what a later purchase adds onto");
    }

    [Fact]
    public async Task ATradeDeliversEachItemWithItsIdentityAndNeverOntoAnotherStack()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, taker, giverClient, takerClient) = OpenTrade(provider);
        StockPotions(giver);
        StockSword(giver);

        // The taker already carries potions that expire; fresh ones must not join that stack.
        var expiring = BagSlot(taker, 0);
        expiring.ItemId = Potion;
        expiring.Count = 2;
        expiring.ExpiresAt = OldPotionExpiry;

        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, 3));
        await SendAsync(provider, giverClient, AddPacket(SwordPos, Sword, 1));
        await SendAsync(provider, takerClient, DecidePacket());
        await SendAsync(provider, giverClient, DecidePacket());

        expiring.Count.Should().Be(2);
        expiring.ExpiresAt.Should().Be(OldPotionExpiry);
        var fresh = BagSlots(taker).Single(slot => slot.ItemId == Potion && slot.ExpiresAt == 0);
        fresh.Count.Should().Be(3);
        var sword = BagSlots(taker).Single(slot => slot.ItemId == Sword);
        sword.Flag.Should().Be((byte)ItemFlag.NotBound);
        sword.ExpiresAt.Should().Be(SwordExpiry);
        sword.Durability.Should().Be(77);

        BagSlot(giver, PotionPos).Count.Should().Be(2);
        BagSlot(giver, SwordPos).IsEmpty.Should().BeTrue();
        CountInBag(giver, Sword).Should().Be(0);
    }

    [Fact]
    public async Task OfferedGoldThatNoLongerFitsInThePurseGoesToTheWarehouseOnCancel()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, _, giverClient, _) = OpenTrade(provider);
        giver.Money = 1_000;

        await SendAsync(provider, giverClient, AddPacket(0, InventoryConstants.ItemGold, 1_000));
        // Gold reached the purse by some route while the offer was held.
        giver.Money = ExchangePacketConstants.CoinMax - 100;
        await SendAsync(provider, giverClient, CancelPacket());

        giver.Money.Should().Be(ExchangePacketConstants.CoinMax, "the purse never goes past the cap");
        giver.WarehouseMoney.Should().Be(900, "the offered gold that no longer fits is kept, not destroyed");
    }

    [Fact]
    public async Task DyingToAMonsterMidTradeGivesBothPlayersTheirOffersBack()
    {
        using var provider = CreateProvider(_ => { }, gameData =>
        {
            StockItems(gameData);
            gameData.GetMaxExpForLevel(Arg.Any<byte>()).Returns(1000L);
            gameData.PremiumItemTable.Returns(new Dictionary<byte, PremiumItemData>());
        });
        var (giver, taker, giverClient, takerClient) = OpenTrade(provider);
        StockPotions(giver);
        giver.Money = 1_000;
        StockSword(taker);

        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, PotionStack));
        await SendAsync(provider, giverClient, AddPacket(0, InventoryConstants.ItemGold, 400));
        await SendAsync(provider, takerClient, AddPacket(SwordPos, Sword, 1));

        giver.Hp = 0;
        await provider.GetRequiredService<INpcAiDeathService>()
            .HandlePlayerKilledByNpcAsync(giver, new NpcInstance { NpcType = 12 });

        CountInBag(giver, Potion).Should().Be(PotionStack);
        giver.Money.Should().Be(1_000);
        CountInBag(taker, Sword).Should().Be(1);
        giver.Trade.IsTrading.Should().BeFalse();
        taker.Trade.IsTrading.Should().BeFalse();
        taker.Trade.ExchangeItemList.Should().BeEmpty();
    }

    [Fact]
    public async Task ADeclinedRequestGivesBackWhatWasOfferedBeforeTheAnswer()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var askerClient = MakeClient();
        var partnerClient = MakeClient();
        var asker = MakeSession(sessionManager, askerClient, GiverId);
        MakeSession(sessionManager, partnerClient, TakerId);
        StockPotions(asker);

        var request = new Packet(GameOpcodes.GS_EXCHANGE);
        request.WriteByte(ExchangePacketConstants.ExchangeRequest);
        request.WriteInt(TakerId);
        await SendAsync(provider, askerClient, request);
        await SendAsync(provider, askerClient, AddPacket(PotionPos, Potion, PotionStack));

        var decline = new Packet(GameOpcodes.GS_EXCHANGE);
        decline.WriteByte(ExchangePacketConstants.ExchangeAgree);
        decline.WriteByte(0);
        await SendAsync(provider, partnerClient, decline);

        CountInBag(asker, Potion).Should().Be(PotionStack);
        asker.Trade.ExchangeItemList.Should().BeEmpty();
        asker.Trade.IsTrading.Should().BeFalse();
    }

    private static void StockItems(IGameDataService gameData)
    {
        gameData.GetItem(Potion).Returns(new ItemData { Num = Potion, Countable = 1, Weight = 1 });
        gameData.GetItem(Sword).Returns(new ItemData { Num = Sword, Countable = 0, Weight = 1, Duration = 100 });
        gameData.GetItem(Pickup).Returns(new ItemData { Num = Pickup, Countable = 1, Weight = 1 });
    }

    private static void StockPotions(UserSession session)
    {
        var slot = BagSlot(session, PotionPos);
        slot.ItemId = Potion;
        slot.Count = PotionStack;
    }

    private static void StockSword(UserSession session)
    {
        var slot = BagSlot(session, SwordPos);
        slot.ItemId = Sword;
        slot.Count = 1;
        slot.Durability = 77;
        slot.Flag = (byte)ItemFlag.NotBound;
        slot.ExpiresAt = SwordExpiry;
    }

    private static ItemSlot BagSlot(UserSession session, byte pos) =>
        session.Inventory[InventoryConstants.InventoryStart + pos];

    private static IEnumerable<ItemSlot> BagSlots(UserSession session) =>
        session.Inventory.Skip(InventoryConstants.InventoryStart).Take(InventoryConstants.HaveMax);

    private static int CountInBag(UserSession session, int itemId) =>
        BagSlots(session).Where(slot => slot.ItemId == itemId).Sum(slot => slot.Count);

    private static Packet AddPacket(byte pos, int itemId, int count)
    {
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(ExchangePacketConstants.ExchangeAdd);
        packet.WriteByte(pos);
        packet.WriteInt(itemId);
        packet.WriteInt(count);
        return packet;
    }

    private static Packet DecidePacket()
    {
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(ExchangePacketConstants.ExchangeDecide);
        return packet;
    }

    private static Packet CancelPacket()
    {
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(ExchangePacketConstants.ExchangeCancel);
        return packet;
    }

    private static Packet RemovePacket(byte pos, int itemId)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        packet.WriteByte((byte)ItemRemoveSource.Inventory);
        packet.WriteByte(pos);
        packet.WriteInt(itemId);
        packet.ResetOffset();
        return packet;
    }

    private static Task SendAsync(ServiceProvider provider, IClient client, Packet packet)
    {
        packet.ResetOffset();
        return provider.GetRequiredService<IExchangePacketCoordinator>().HandleAsync(client, packet);
    }

    private static (UserSession Giver, UserSession Taker, IClient GiverClient, IClient TakerClient)
        OpenTrade(ServiceProvider provider)
    {
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var giverClient = MakeClient();
        var takerClient = MakeClient();
        var giver = MakeSession(sessionManager, giverClient, GiverId);
        var taker = MakeSession(sessionManager, takerClient, TakerId);

        giver.Trade.ExchangeUser = taker.CharacterId;
        taker.Trade.ExchangeUser = giver.CharacterId;
        giver.InitExchange(true);
        taker.InitExchange(true);
        return (giver, taker, giverClient, takerClient);
    }

    private static IClient MakeClient()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return client;
    }

    private static UserSession MakeSession(SessionManager sessionManager, IClient client, int characterId)
    {
        var session = sessionManager.CreateSession(client, characterId, accountId: characterId + 1000);
        session.Name = $"Trader{characterId}";
        session.Nation = AccountNation.Karus;
        session.ZoneId = BattleZoneManager.ZONE_MORADON;
        session.X = 816f;
        session.Z = 531f;
        session.Hp = 100;
        session.MaxHp = 100;
        session.Level = 10;
        session.Stats = new DerivedStats { MaxWeight = 10_000 };
        return session;
    }
}
