using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ItemAuthorityTests : GameTestBase
{
    private const int Ring = 310110001;
    private const int OtherRing = 310120001;
    private const int HighRing = 310210001;
    private const int Sword = 110110001;
    private const int Potion = 389010000;
    private const int Untradeable = 389020000;
    private const byte RingSlot = 12;
    private const byte OneHandRight = 1;
    private const byte HighLevel = 70;
    private const byte PlayerLevel = 30;
    private const byte UntradeableRace = 20;
    private const byte Moradon = (byte)ZoneId.Moradon;
    private const float Here = 500f;
    private const byte BagFirst = 0;
    private const byte BagSecond = 1;
    private const short WornDurability = 37;
    private const long RentalEnds = 1_900_000_000;
    private const ushort Stack = 5;
    private const int SealedCharacter = 4242;
    private const byte Moved = 1;
    private const int Purse = 1000;
    private const int GenieGold = 20_000;
    private const byte GenieClaim = 2;
    private const int ShortReply = 8;
    private const byte NpcSwap = 3;
    private const int CosHelmet = 900_100_000;
    private const int OtherCosHelmet = 900_110_000;
    private const int CosWings = 900_200_000;
    private const byte CospreHelmetSlot = 107;
    private const byte CospreWingsSlot = 110;
    private const int MagicBag = 700_010_000;
    private const int OtherMagicBag = 700_020_000;
    private const byte BagItemSlot = 25;
    private const byte FirstBagSlot = 0;
    private const ushort Split = 2;
    private const int NotAKeeper = 15_001;
    private const short PlayerMaxHp = 100;
    private const float FarFromTheKeeper = 50f;

    [Theory]
    [InlineData(Sword, false)]
    [InlineData(HighRing, false)]
    [InlineData(OtherRing, true)]
    public async Task UnequippingOntoABagItemPutsItInTheSlotOnlyIfItMayGoThere(int bagItem, bool swapped)
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2001);
        Put(player.Inventory[InventoryConstants.RightRing], Ring);
        Put(Bag(player, BagFirst), bagItem);

        await MoveItem(provider, client, ItemMoveDirection.SlotToInventory, Ring, InventoryConstants.RightRing, BagFirst);

        player.Inventory[InventoryConstants.RightRing].ItemId.Should().Be(swapped ? bagItem : Ring);
        Bag(player, BagFirst).ItemId.Should().Be(swapped ? Ring : bagItem);
    }

    [Fact]
    public async Task AnItemThatCannotBeTradedCannotBeDropped()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2002);
        Put(Bag(player, BagFirst), Sword).Flag = (byte)ItemFlag.Rented;
        Put(Bag(player, BagSecond), Untradeable);

        await Drop(provider, client, BagFirst, Sword, 1);
        await Drop(provider, client, BagSecond, Untradeable, 1);

        Bag(player, BagFirst).ItemId.Should().Be(Sword, "a rented item stays with its renter");
        Bag(player, BagSecond).ItemId.Should().Be(Untradeable);
    }

    [Fact]
    public async Task ADroppedItemIsPickedUpAsItWas()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2003);
        var sword = Put(Bag(player, BagFirst), Sword);
        sword.Durability = WornDurability;
        sword.Flag = (byte)ItemFlag.NotBound;
        var bundles = Bundles(client);

        await Drop(provider, client, BagFirst, Sword, 1);
        var bundle = provider.GetRequiredService<SessionManager>().Regions.GetBundle(bundles.Single())!;
        bundle.DropTimeTicks -= TimeSpan.FromMilliseconds(LootBundle.OwnerExclusiveMs + 1).Ticks;
        await Pick(provider, client, bundle.BundleId, Sword);

        var picked = player.Inventory.Skip(InventoryConstants.InventoryStart).Take(InventoryConstants.HaveMax)
            .Single(slot => slot.ItemId == Sword);
        picked.Durability.Should().Be(WornDurability, "a pickup does not repair");
        picked.Flag.Should().Be((byte)ItemFlag.NotBound);
    }

    [Fact]
    public async Task ADropNamingAnotherItemThanTheSlotHoldsDropsNothing()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2090);
        Put(Bag(player, BagFirst), Untradeable);
        var bundles = Bundles(client);

        await Drop(provider, client, BagFirst, Potion, 1);

        bundles.Should().BeEmpty("the slot holds another item than the one named");
        Bag(player, BagFirst).ItemId.Should().Be(Untradeable);
    }

    [Fact]
    public async Task ATimedItemIsNotDropped()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2004);
        Put(Bag(player, BagFirst), Sword).ExpiresAt = RentalEnds;
        var bundles = Bundles(client);

        await Drop(provider, client, BagFirst, Sword, 1);

        bundles.Should().BeEmpty("a timed item cannot change hands, and a drop hands it to whoever picks it up");
        Bag(player, BagFirst).ItemId.Should().Be(Sword);
    }

    [Fact]
    public async Task AWornOutItemDroppedIsPickedUpWornOut()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2011);
        Put(Bag(player, BagFirst), Sword).Durability = 0;
        var bundles = Bundles(client);

        await Drop(provider, client, BagFirst, Sword, 1);
        var bundle = provider.GetRequiredService<SessionManager>().Regions.GetBundle(bundles.Single())!;
        bundle.DropTimeTicks -= TimeSpan.FromMilliseconds(LootBundle.OwnerExclusiveMs + 1).Ticks;
        await Pick(provider, client, bundle.BundleId, Sword);

        player.Inventory.Skip(InventoryConstants.InventoryStart).Take(InventoryConstants.HaveMax)
            .Single(slot => slot.ItemId == Sword).Durability.Should().Be(0, "dropping a broken item does not repair it");
    }

    [Fact]
    public async Task ANpcSwapKeepsEachItemsExpiry()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2004);
        Put(Bag(player, BagFirst), Sword).ExpiresAt = RentalEnds;
        Put(Bag(player, BagSecond), Ring);
        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(NpcSwap);
        packet.WriteInt(Sword);
        packet.WriteByte(BagFirst);
        packet.WriteByte(BagSecond);

        await provider.GetRequiredService<IItemTradeService>().HandleTradeAsync(client, Reset(packet));

        Bag(player, BagSecond).ItemId.Should().Be(Sword);
        Bag(player, BagSecond).ExpiresAt.Should().Be(RentalEnds);
        Bag(player, BagFirst).ExpiresAt.Should().Be(0, "the ring was never timed");
    }

    [Theory]
    [InlineData(OtherCosHelmet, true)]
    [InlineData(CosWings, false)]
    public async Task TakingOffACospreOntoABagItemSwapsOnlyIfThatItemFitsTheCospreSlot(int bagItem, bool swapped)
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2012);
        Put(player.Inventory[InventoryConstants.CosHelmet], CosHelmet);
        Put(Bag(player, BagFirst), bagItem);

        await MoveItem(provider, client, ItemMoveDirection.CospreToInventory, CosHelmet, InventoryConstants.CosPosHelmet, BagFirst);

        player.Inventory[InventoryConstants.CosHelmet].ItemId.Should().Be(swapped ? bagItem : CosHelmet);
        Bag(player, BagFirst).ItemId.Should().Be(swapped ? CosHelmet : bagItem);
    }

    [Theory]
    [InlineData(OtherMagicBag, true)]
    [InlineData(Potion, false)]
    public async Task TakingOffABagOntoABagItemSwapsOnlyIfThatItemIsABag(int bagItem, bool swapped)
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2013);
        var worn = InventoryConstants.BagSlotStart + FirstBagSlot;
        Put(player.Inventory[worn], MagicBag);
        Put(Bag(player, BagFirst), bagItem);

        await MoveItem(provider, client, ItemMoveDirection.BagSlotToInventory, MagicBag, FirstBagSlot, BagFirst);

        player.Inventory[worn].ItemId.Should().Be(swapped ? bagItem : MagicBag);
        Bag(player, BagFirst).ItemId.Should().Be(swapped ? MagicBag : bagItem);
    }

    [Fact]
    public async Task PartOfATimedStackIsNotSplitOntoAPermanentOne()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2014);
        var timed = Put(Bag(player, BagFirst), Potion);
        timed.Count = Stack;
        timed.ExpiresAt = RentalEnds;
        Put(Bag(player, BagSecond), Potion).Count = Stack;

        await MoveItem(provider, client, ItemMoveDirection.InventoryToInventory, Potion, BagFirst, BagSecond, Split);

        Bag(player, BagFirst).Count.Should().Be(Stack);
        Bag(player, BagSecond).Count.Should().Be(Stack, "potions that expire are not split onto ones that do not");
    }

    [Fact]
    public void ABundleEntryIsClaimedOnlyIfItIsStillTheOneThatWasSeen()
    {
        var bundle = new LootBundle();
        var first = new LootItem { ItemId = Potion, Count = 1 };
        var second = new LootItem { ItemId = Potion, Count = Stack };
        var third = new LootItem { ItemId = Potion, Count = Split };
        bundle.Items.AddRange([first, second, third]);
        var seen = bundle.SnapshotItems()[1];

        bundle.TryClaimSlot(0, first).Should().BeTrue();

        bundle.TryClaimSlot(1, seen).Should().BeFalse("another entry of the same item now sits where this one was seen");
        bundle.Items.Should().Equal(second, third);
        bundle.TryClaimSlot(0, seen).Should().BeTrue();
    }

    [Fact]
    public async Task AStackMovedOntoItsOwnSlotStaysWhole()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2015);
        Put(Bag(player, BagFirst), Potion).Count = Stack;

        await MoveItem(provider, client, ItemMoveDirection.InventoryToInventory, Potion, BagFirst, BagFirst);

        Bag(player, BagFirst).ItemId.Should().Be(Potion, "a stack merged into itself would be cleared");
        Bag(player, BagFirst).Count.Should().Be(Stack);
    }

    [Fact]
    public async Task ARingHoldingASealedCharacterIsNotDestroyed()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2016);
        var ring = Put(Bag(player, BagFirst), InventoryConstants.CypherRing);
        ring.UniqueId = SealedCharacter;
        var remove = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        remove.WriteByte((byte)ItemRemoveSource.Inventory);
        remove.WriteByte(BagFirst);
        remove.WriteInt(InventoryConstants.CypherRing);

        await provider.GetRequiredService<IItemRemoveService>().HandleAsync(client, Reset(remove));

        ring.ItemId.Should().Be(InventoryConstants.CypherRing, "destroying the ring would lose the character sealed in it");
        ring.UniqueId.Should().Be(SealedCharacter);
    }

    [Fact]
    public async Task ATimedStackDoesNotMergeIntoAPermanentOne()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2005);
        var timed = Put(Bag(player, BagFirst), Potion);
        timed.Count = Stack;
        timed.ExpiresAt = RentalEnds;
        Put(Bag(player, BagSecond), Potion).Count = Stack;

        await MoveItem(provider, client, ItemMoveDirection.InventoryToInventory, Potion,
            BagFirst, BagSecond);

        Bag(player, BagSecond).Count.Should().Be(Stack, "potions that expire are not added to ones that do not");
    }

    [Fact]
    public async Task StoringAnItemKeepsItsExpiryAndDoesNotRelabelAStack()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2006);
        StandAtWarehouseKeeper(provider, client);
        Put(Bag(player, BagFirst), Sword).ExpiresAt = RentalEnds;
        var sealedPotions = Put(Bag(player, BagSecond), Potion);
        sealedPotions.Count = Stack;
        sealedPotions.Flag = (byte)ItemFlag.Bound;
        Put(player.Warehouse[1], Potion).Count = Stack;

        await Store(provider, client, Sword, BagFirst, 0, 1);
        await Store(provider, client, Potion, BagSecond, 1, Stack);

        player.Warehouse[0].ExpiresAt.Should().Be(RentalEnds);
        player.Warehouse[1].Flag.Should().Be(0, "bound potions are not poured into a stack of free ones");
        player.Warehouse[1].Count.Should().Be(Stack);
    }

    [Theory]
    [InlineData(true, 0f, true)]
    [InlineData(true, 50f, false)]
    [InlineData(false, 0f, false)]
    public async Task TheWarehouseOpensOnlyAtAKeeperNearby(bool talkedToKeeper, float keeperDistance, bool opened)
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2007);
        if (talkedToKeeper)
        {
            StandAtWarehouseKeeper(provider, client);
            provider.GetRequiredService<SessionManager>().Regions.GetNpc(player.Quest.EventNpcUniqueId)!.X += keeperDistance;
        }
        var sent = Sent(client);
        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte((byte)WarehouseSubOpcode.Open);

        await provider.GetRequiredService<IWarehousePacketCoordinator>().HandleAsync(client, Reset(packet));

        sent.Any(reply => reply.GetOpcode() == (byte)GameOpcodes.GS_WAREHOUSE && reply.GetLength() > ShortReply).Should().Be(opened);
    }

    [Fact]
    public async Task TheWarehouseDoesNotOpenAtAnNpcThatKeepsNone()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2015);
        TalkTo(provider, player, NotAKeeper);
        var sent = Sent(client);

        await OpenWarehouse(provider, client);

        sent.Any(reply => reply.GetOpcode() == (byte)GameOpcodes.GS_WAREHOUSE && reply.GetLength() > ShortReply).Should().BeFalse();
    }

    [Fact]
    public async Task StorageOpenedAtAKeeperStillWorksAfterTalkingToAnotherNpc()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2016);
        StandAtWarehouseKeeper(provider, client);
        await OpenWarehouse(provider, client);
        TalkTo(provider, player, NotAKeeper);
        Put(Bag(player, BagFirst), Sword);

        await Store(provider, client, Sword, BagFirst, 0, 1);

        player.Warehouse[0].ItemId.Should().Be(Sword);
    }

    [Fact]
    public async Task StorageStopsWorkingOnceThePlayerWalksAwayFromTheKeeper()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2018);
        StandAtWarehouseKeeper(provider, client);
        await OpenWarehouse(provider, client);
        player.X += FarFromTheKeeper;
        Put(Bag(player, BagFirst), Sword);

        await Store(provider, client, Sword, BagFirst, 0, 1);

        player.Warehouse[0].IsEmpty.Should().BeTrue();
        Bag(player, BagFirst).ItemId.Should().Be(Sword);
    }

    [Fact]
    public async Task TheRouletteHandsOutNoFreeSpins()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2008);
        player.Money = Purse;
        var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
        packet.WriteByte((byte)EventBoardSubOpcode.RouletteSpin);

        await provider.GetRequiredService<IRoulettePacketCoordinator>().HandleAsync(client, Reset(packet));

        player.Money.Should().Be(Purse, "a spin needs a coin, and no one is given coins for nothing");
    }

    [Fact]
    public async Task TheGenieGoldComesOnceADayAndTheDayIsSaved()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2009);
        player.Money = Purse;
        var genie = provider.GetRequiredService<IGeniePacketCoordinator>();

        await genie.HandleAsync(client, Claim());
        await genie.HandleAsync(client, Claim());

        player.Money.Should().Be(Purse + GenieGold);
        var saved = new Common.Domain.Entities.Character();
        new UserSessionCharacterMapper().ApplyToCharacter(player, saved);
        saved.GenieRewardClaimedOn.Should().Be(DateTime.UtcNow.Date, "a restart must not forget today's claim");
    }

    [Fact]
    public async Task AGenieClaimLoadedWithTheCharacterCountsForToday()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2010);
        var character = new Common.Domain.Entities.Character
        {
            Name = player.Name, Money = Purse, GenieRewardClaimedOn = DateTime.UtcNow.Date,
        };
        new UserSessionCharacterMapper().HydrateSession(player, character, new Common.Domain.Entities.Account(),
            new Common.Domain.Entities.Warehouse(), PlayerMaxHp, PlayerMaxHp, provider.GetRequiredService<Common.Domain.Services.IGameDataService>());

        await provider.GetRequiredService<IGeniePacketCoordinator>().HandleAsync(client, Claim());

        player.Money.Should().Be(Purse);
    }

    [Fact]
    public async Task AGenieClaimThatWouldOverfillThePurseIsRefusedAndKeepsTheDay()
    {
        using var provider = Provider();
        var (player, client) = Player(provider, 2017);
        player.Money = ExchangePacketConstants.CoinMax - GenieGold + 1;

        await provider.GetRequiredService<IGeniePacketCoordinator>().HandleAsync(client, Claim());

        player.Money.Should().Be(ExchangePacketConstants.CoinMax - GenieGold + 1);
        player.GenieRewardClaimedOn.Should().BeNull("a refused claim leaves the day's reward to take later");
    }

    private static ServiceProvider Provider() => CreateProvider(_ => { }, gameData =>
    {
        foreach (var (id, level) in new[] { (Ring, (byte)1), (OtherRing, (byte)1), (HighRing, HighLevel) })
            gameData.GetItem(id).Returns(new ItemData
            {
                Num = id, Kind = (byte)ItemKind.Ring, Slot = RingSlot, ReqLevel = level, Duration = 100,
            });
        gameData.GetItem(Sword).Returns(new ItemData
        {
            Num = Sword, Kind = (byte)ItemKind.SwordOneHand, Slot = OneHandRight, Duration = 100, Damage = 10,
        });
        gameData.GetItem(Potion).Returns(new ItemData { Num = Potion, Countable = 1, Duration = 1 });
        gameData.GetItem(Untradeable).Returns(new ItemData { Num = Untradeable, Race = UntradeableRace, Duration = 1 });
        foreach (var (id, slot) in new[] { (CosHelmet, CospreHelmetSlot), (OtherCosHelmet, CospreHelmetSlot), (CosWings, CospreWingsSlot) })
            gameData.GetItem(id).Returns(new ItemData { Num = id, Slot = slot, Duration = 1 });
        foreach (var id in new[] { MagicBag, OtherMagicBag })
            gameData.GetItem(id).Returns(new ItemData { Num = id, Slot = BagItemSlot, Duration = 1 });
    });

    private static (UserSession Player, IClient Client) Player(ServiceProvider provider, int id)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var player = sessions.CreateSession(client, id, id + 100);
        player.Name = $"Holder{id}";
        player.Level = PlayerLevel;
        player.ZoneId = Moradon;
        player.Nation = AccountNation.Karus;
        player.Hp = player.MaxHp = 100;
        player.X = Here;
        player.Z = Here;
        sessions.Regions.AddToRegion(player);
        return (player, client);
    }

    private static List<Packet> Sent(IClient client)
    {
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return sent;
    }

    private static List<int> Bundles(IClient client)
    {
        var bundles = new List<int>();
        client.SendPacket(Arg.Do<Packet>(packet =>
        {
            if (packet.GetOpcode() != (byte)GameOpcodes.GS_ITEM_DROP)
                return;
            packet.ResetOffset();
            packet.ReadInt();
            bundles.Add(packet.ReadInt());
        }), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return bundles;
    }

    private static ItemSlot Bag(UserSession player, byte position) => player.Inventory[InventoryConstants.InventoryStart + position];

    private static ItemSlot Put(ItemSlot slot, int itemId)
    {
        slot.ItemId = itemId;
        slot.Count = 1;
        slot.Durability = 100;
        return slot;
    }

    private static Task MoveItem(
        ServiceProvider provider, IClient client, ItemMoveDirection direction, int itemId, int source, int destination,
        ushort? amount = null)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_MOVE);
        packet.WriteByte(Moved);
        packet.WriteByte((byte)direction);
        packet.WriteInt(itemId);
        packet.WriteByte((byte)source);
        packet.WriteByte((byte)destination);
        if (amount is { } split)
            packet.WriteUShort(split);
        return provider.GetRequiredService<IItemPacketCoordinator>().HandleMoveAsync(client, Reset(packet));
    }

    private static Task Drop(ServiceProvider provider, IClient client, byte position, int itemId, ushort count)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_DROP);
        packet.WriteByte(position);
        packet.WriteInt(itemId);
        packet.WriteUShort(count);
        return provider.GetRequiredService<ILootPacketCoordinator>().HandleItemDropAsync(client, Reset(packet));
    }

    private static Task Pick(ServiceProvider provider, IClient client, int bundleId, int itemId)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_GET);
        packet.WriteInt(bundleId);
        packet.WriteInt(itemId);
        packet.WriteUShort(0);
        return provider.GetRequiredService<ILootPacketCoordinator>().HandleItemGetAsync(client, Reset(packet));
    }

    private static Task Store(ServiceProvider provider, IClient client, int itemId, byte from, byte to, int count)
    {
        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte((byte)WarehouseSubOpcode.Input);
        packet.WriteInt(0);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(from);
        packet.WriteByte(to);
        packet.WriteInt(count);
        return provider.GetRequiredService<IWarehousePacketCoordinator>().HandleAsync(client, Reset(packet));
    }

    private static void TalkTo(ServiceProvider provider, UserSession player, int npcId)
    {
        var npc = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            NpcId = npcId, Name = "Merchant", ZoneId = player.ZoneId, X = player.X, Z = player.Z,
            SpawnX = player.X, SpawnZ = player.Z, Hp = 1, MaxHp = 1,
        });
        player.Quest.EventNpcUniqueId = npc.UniqueId;
    }

    private static Task OpenWarehouse(ServiceProvider provider, IClient client)
    {
        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte((byte)WarehouseSubOpcode.Open);
        return provider.GetRequiredService<IWarehousePacketCoordinator>().HandleAsync(client, Reset(packet));
    }

    private static Packet Claim()
    {
        var packet = new Packet(GameOpcodes.GS_GENIE);
        packet.WriteByte(GenieClaim);
        return Reset(packet);
    }

    private static Packet Reset(Packet packet)
    {
        packet.ResetOffset();
        return packet;
    }
}
