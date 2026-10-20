using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
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
    private const int SomeoneElse = 92003;
    private const int Purse = 1_000;
    private const int OfferedGold = 400;

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
        var purse = Sent(giverClient).Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_GOLD_CHANGE);
        purse.ResetOffset();
        purse.ReadByte();
        purse.ReadInt().Should().Be(900);
        purse.ReadInt().Should().Be(ExchangePacketConstants.CoinMax, "the client's purse is set to what it really holds");
    }

    [Fact]
    public async Task APartnersClientIsToldWhereTheirCancelledOfferReallyWent()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (_, taker, giverClient, takerClient) = OpenTrade(provider);
        StockPotions(taker);

        await SendAsync(provider, takerClient, AddPacket(PotionPos, Potion, PotionStack));
        // The same potion, but timed, lands in the freed slot; the offer must not join it.
        var filled = BagSlot(taker, PotionPos);
        filled.ItemId = Potion;
        filled.Count = 1;
        filled.ExpiresAt = OldPotionExpiry;
        await SendAsync(provider, giverClient, CancelPacket());

        BagSlot(taker, 0).Count.Should().Be(PotionStack);
        var sent = Sent(takerClient);
        var cancelAt = sent.FindIndex(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE && FirstByte(p) == ExchangePacketConstants.ExchangeCancel);
        var resyncAt = sent.FindIndex(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        cancelAt.Should().BeGreaterThanOrEqualTo(0);
        resyncAt.Should().BeGreaterThan(cancelAt, "the client restores its own view on the cancel, then learns the truth");
        CountChangePackets.Positions(sent).Should().Contain([PotionPos, 0]);
    }

    [Fact]
    public async Task APlayersOwnCancelIsNotEchoedBackToThem()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, _, giverClient, takerClient) = OpenTrade(provider);
        StockPotions(giver);
        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, 3));

        await SendAsync(provider, giverClient, CancelPacket());

        Sent(giverClient).Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE
            && FirstByte(p) == ExchangePacketConstants.ExchangeCancel,
            "its client closed the trade itself, and an echo could land on the next request");
        Sent(takerClient).Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE
            && FirstByte(p) == ExchangePacketConstants.ExchangeCancel);
        Sent(giverClient).Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE,
            "the bag is still resynced after the player's own cancel");
        BagSlot(giver, PotionPos).Count.Should().Be(PotionStack);
    }

    [Fact]
    public async Task AServerCancelAndAFinishingTradeNeverBothHappen()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var transfer = provider.GetRequiredService<IExchangeTransferService>();
        UserSession? partner = null;
        var decided = false;
        var giverClient = Substitute.For<IClient>();
        giverClient.Id.Returns(Guid.NewGuid());
        giverClient.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            // The partner's acceptance lands while the server is telling the player the trade is off.
            var packet = call.Arg<Packet>();
            if (!decided && packet.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE && FirstByte(packet) == ExchangePacketConstants.ExchangeCancel)
            {
                decided = true;
                transfer.DecideAsync(partner!).GetAwaiter().GetResult();
            }
            return Task.CompletedTask;
        });
        var giver = MakeSession(sessionManager, giverClient, GiverId);
        var taker = MakeSession(sessionManager, MakeClient(), TakerId);
        partner = taker;
        giver.Trade.ExchangeUser = taker.CharacterId;
        taker.Trade.ExchangeUser = giver.CharacterId;
        giver.InitExchange(true);
        taker.InitExchange(true);
        StockSword(giver);
        await SendAsync(provider, giverClient, AddPacket(SwordPos, Sword, 1));
        giver.Trade.ExchangeOk = true;

        await provider.GetRequiredService<IExchangeLifecycleService>().CancelAsync(giver, isOnDeath: true);

        decided.Should().BeTrue();
        var told = Sent(giverClient).Any(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE && FirstByte(p) == ExchangePacketConstants.ExchangeCancel);
        var traded = CountInBag(taker, Sword) == 1;
        (told && traded).Should().BeFalse("a trade is either called off or completed, and the player is told which");
        told.Should().BeTrue("here the cancel came first");
        traded.Should().BeFalse();
        CountInBag(giver, Sword).Should().Be(1);
        giver.Trade.IsTrading.Should().BeFalse();
        taker.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task ACancelTheServerStartsReachesThePlayerBeforeTheirBagIsResynced()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, taker, giverClient, _) = OpenTrade(provider);
        StockPotions(giver);
        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, 3));

        // The partner walks off; the next offer finds them out of range and the server cancels.
        taker.X += 1_000f;
        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, 1));

        giver.Trade.IsTrading.Should().BeFalse();
        var sent = Sent(giverClient);
        var cancelAt = sent.FindIndex(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE && FirstByte(p) == ExchangePacketConstants.ExchangeCancel);
        var resyncAt = sent.FindIndex(p => p.GetOpcode() == (byte)GameOpcodes.GS_ITEM_COUNT_CHANGE);
        cancelAt.Should().BeGreaterThanOrEqualTo(0, "the player's own client has to close the trade and put its offer back");
        resyncAt.Should().BeGreaterThan(cancelAt);
        BagSlot(giver, PotionPos).Count.Should().Be(PotionStack);
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
    public async Task ADeadTraderWhoLogsOutGetsTheirOfferBackBeforeTheSave()
    {
        using var provider = CreateProvider(db => db.Characters.Add(new Character
        {
            Id = GiverId, AccountId = GiverId + 1000, Name = $"Trader{GiverId}", Race = 1, Class = 101, Face = 1, Hair = 1,
            Level = 10, Hp = 100, Mp = 100, MapId = BattleZoneManager.ZONE_MORADON,
            Items = new byte[InventoryConstants.InventoryTotal * 8], SkillPointData = new byte[9],
        }), StockItems);
        var (giver, taker, giverClient, _) = OpenTrade(provider);
        giverClient.CharacterId = GiverId;
        StockSword(giver);

        await SendAsync(provider, giverClient, AddPacket(SwordPos, Sword, 1));
        // Killed by something that did not end the trade, then gone.
        giver.Hp = 0;
        await provider.GetRequiredService<ISessionTerminationService>().LogoutAsync(giverClient);

        CountInBag(giver, Sword).Should().Be(1);
        taker.Trade.IsTrading.Should().BeFalse();
        await using var scope = provider.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Characters.SingleAsync(c => c.Id == GiverId);
        row.Items.Should().Equal(giver.SerializeItems(), "the offered sword is saved back in the bag");
    }

    [Fact]
    public async Task NothingCanBeOfferedBeforeTheRequestIsAnswered()
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
        asker.Trade.ExchangeItemList.Should().BeEmpty("the partner has not opened the trade yet");

        var decline = new Packet(GameOpcodes.GS_EXCHANGE);
        decline.WriteByte(ExchangePacketConstants.ExchangeAgree);
        decline.WriteByte(0);
        await SendAsync(provider, partnerClient, decline);

        CountInBag(asker, Potion).Should().Be(PotionStack);
        asker.Trade.ExchangeItemList.Should().BeEmpty();
        asker.Trade.IsTrading.Should().BeFalse();
    }

    [Fact]
    public async Task AConfirmSentBeforeTheTradeIsAcceptedCountsForNothing()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var askerClient = MakeClient();
        var partnerClient = MakeClient();
        var asker = MakeSession(sessionManager, askerClient, GiverId);
        var partner = MakeSession(sessionManager, partnerClient, TakerId);
        StockPotions(partner);

        await SendAsync(provider, askerClient, RequestPacket(TakerId));
        await SendAsync(provider, askerClient, DecidePacket());
        await SendAsync(provider, partnerClient, AgreePacket(1));
        await SendAsync(provider, partnerClient, AddPacket(PotionPos, Potion, PotionStack));
        await SendAsync(provider, partnerClient, DecidePacket());

        CountInBag(asker, Potion).Should().Be(0, "the asker's early confirm must not close the trade the partner just filled");
        partner.Trade.ExchangeItemList.Should().ContainSingle();
    }

    [Fact]
    public async Task APartnerIsNotToldOfAConfirmBeforeTheyAccept()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var askerClient = MakeClient();
        var partnerClient = MakeClient();
        MakeSession(sessionManager, askerClient, GiverId);
        MakeSession(sessionManager, partnerClient, TakerId);

        await SendAsync(provider, askerClient, RequestPacket(TakerId));
        await SendAsync(provider, askerClient, DecidePacket());

        Sent(partnerClient).Should().NotContain(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE
            && FirstByte(p) == ExchangePacketConstants.ExchangeOtherDecide);
    }

    [Fact]
    public async Task AnAcceptThatFindsTheAskerGoneElsewhereClosesTheWindow()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var askerClient = MakeClient();
        var partnerClient = MakeClient();
        var asker = MakeSession(sessionManager, askerClient, GiverId);
        var partner = MakeSession(sessionManager, partnerClient, TakerId);
        await SendAsync(provider, askerClient, RequestPacket(TakerId));
        asker.Trade.ExchangeUser = SomeoneElse;

        await SendAsync(provider, partnerClient, AgreePacket(1));

        partner.Trade.IsTrading.Should().BeFalse();
        Sent(partnerClient).Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE
            && FirstByte(p) == ExchangePacketConstants.ExchangeCancel);
    }

    [Fact]
    public async Task AConfirmOnATradeThePartnerHasLeftClosesTheWindow()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, taker, giverClient, _) = OpenTrade(provider);
        StockPotions(giver);
        await SendAsync(provider, giverClient, AddPacket(PotionPos, Potion, PotionStack));
        taker.Trade.ExchangeUser = SomeoneElse;

        await SendAsync(provider, giverClient, DecidePacket());

        giver.Trade.IsTrading.Should().BeFalse();
        CountInBag(giver, Potion).Should().Be(PotionStack);
        Sent(giverClient).Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_EXCHANGE
            && FirstByte(p) == ExchangePacketConstants.ExchangeCancel);
    }

    [Fact]
    public async Task AnOfferThatComesBackToTheWarehouseIsAnnounced()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, _, giverClient, _) = OpenTrade(provider);
        StockSword(giver);
        await SendAsync(provider, giverClient, AddPacket(SwordPos, Sword, 1));
        foreach (var slot in BagSlots(giver))
        {
            slot.ItemId = Potion;
            slot.Count = 1;
        }
        var before = Sent(giverClient).Count;

        await SendAsync(provider, giverClient, CancelPacket());

        giver.Warehouse.Should().Contain(slot => slot.ItemId == Sword);
        Sent(giverClient).Skip(before).Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_CHAT,
            "the player is told where the item went instead of watching it vanish");
    }

    [Fact]
    public async Task ASaveDuringATradeKeepsWhatIsOffered()
    {
        using var provider = CreateProvider(_ => { }, StockItems);
        var (giver, _, giverClient, _) = OpenTrade(provider);
        StockSword(giver);
        giver.Money = Purse;
        await SendAsync(provider, giverClient, AddPacket(SwordPos, Sword, 1));
        await SendAsync(provider, giverClient, AddPacket(0, InventoryConstants.ItemGold, OfferedGold));

        var saved = new Character();
        new UserSessionCharacterMapper().ApplyToCharacter(giver, saved);

        var items = new ItemSlot[InventoryConstants.InventoryTotal];
        for (var i = 0; i < items.Length; i++)
            items[i] = new ItemSlot();
        UserSessionBinaryState.LoadItems(items, saved.Items);
        items[InventoryConstants.InventoryStart + SwordPos].ItemId.Should().Be(Sword, "a crash mid-trade must not lose the offer");
        items[InventoryConstants.InventoryStart + SwordPos].ExpiresAt.Should().Be(SwordExpiry);
        saved.Money.Should().Be(Purse);
        BagSlot(giver, SwordPos).IsEmpty.Should().BeTrue("the live bag still has the offer in escrow");
    }

    private static Packet RequestPacket(int partnerId)
    {
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(ExchangePacketConstants.ExchangeRequest);
        packet.WriteInt(partnerId);
        return packet;
    }

    private static Packet AgreePacket(byte agreed)
    {
        var packet = new Packet(GameOpcodes.GS_EXCHANGE);
        packet.WriteByte(ExchangePacketConstants.ExchangeAgree);
        packet.WriteByte(agreed);
        return packet;
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

    private static List<Packet> Sent(IClient client) =>
        client.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IClient.SendPacket))
            .Select(call => (Packet)call.GetArguments()[0]!)
            .ToList();

    private static byte FirstByte(Packet packet)
    {
        packet.ResetOffset();
        return packet.ReadByte();
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
