using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

// An NPC purchase or sale checks every line before it changes anything, then applies all of
// them and settles the gold once.
public class NpcTradeAtomicityTests : GameTestBase
{
    private const byte Buy = 1;
    private const byte Sell = 2;
    private const int SellingGroup = 1;
    private const int Potion = 389_010_000;
    private const int Arrow = 391_010_000;
    private const int PotionPrice = 100;
    private const int ArrowPrice = 10;
    private const int StartingGold = 1_000;

    [Fact]
    public async Task ABuyWhoseLaterLineIsRefusedGivesNothing()
    {
        using var provider = Provider();
        var (session, client, npc) = Shopper(provider);

        await TradeAsync(provider, client, npc, Buy, (Potion, 0, 5), (Arrow, 1, 0));

        Bag(session, 0).IsEmpty.Should().BeTrue("the first line must not be handed out when a later one is refused");
        Bag(session, 1).IsEmpty.Should().BeTrue();
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task ABuyThatCannotBePaidInFullGivesNothing()
    {
        using var provider = Provider();
        var (session, client, npc) = Shopper(provider);

        await TradeAsync(provider, client, npc, Buy, (Potion, 0, 5), (Potion, 1, 6));

        Bag(session, 0).IsEmpty.Should().BeTrue();
        Bag(session, 1).IsEmpty.Should().BeTrue();
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task ABuyIntoAnEmptySlotSetsItsCountInsteadOfAddingToALeftover()
    {
        using var provider = Provider();
        var (session, client, npc) = Shopper(provider);
        Bag(session, 0).Count = 7;

        await TradeAsync(provider, client, npc, Buy, (Arrow, 0, 1));

        Bag(session, 0).ItemId.Should().Be(Arrow);
        Bag(session, 0).Count.Should().Be(1);
        session.Money.Should().Be(StartingGold - ArrowPrice);
    }

    [Fact]
    public async Task ASaleWhoseLaterLineIsRefusedTakesNothing()
    {
        using var provider = Provider();
        var (session, client, npc) = Shopper(provider);
        Stock(Bag(session, 0), Potion, 2);

        await TradeAsync(provider, client, npc, Sell, (Potion, 0, 2), (Arrow, 1, 1));

        Bag(session, 0).ItemId.Should().Be(Potion, "the first line must not be taken when a later one is refused");
        Bag(session, 0).Count.Should().Be(2);
        session.Repurchases.Should().BeEmpty();
        session.Money.Should().Be(StartingGold);
    }

    [Fact]
    public async Task ASaleCannotSellTheSameStackTwice()
    {
        using var provider = Provider();
        var (session, client, npc) = Shopper(provider);
        Stock(Bag(session, 0), Potion, 2);

        await TradeAsync(provider, client, npc, Sell, (Potion, 0, 2), (Potion, 0, 2));

        Bag(session, 0).Count.Should().Be(2);
        session.Money.Should().Be(StartingGold);
    }

    private static async Task TradeAsync(
        ServiceProvider provider, IClient client, NpcInstance npc, byte type,
        params (int ItemId, byte Pos, ushort Count)[] lines)
    {
        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(type);
        packet.WriteInt(SellingGroup);
        packet.WriteInt(npc.UniqueId);
        packet.WriteByte((byte)lines.Length);
        foreach (var (itemId, pos, count) in lines)
        {
            packet.WriteInt(itemId);
            packet.WriteByte(pos);
            packet.WriteUShort(count);
            if (type == Buy)
            {
                packet.WriteByte(0);
                packet.WriteByte(0);
            }
        }

        packet.ResetOffset();
        await provider.GetRequiredService<IItemTradeService>().HandleTradeAsync(client, packet);
    }

    private static ServiceProvider Provider() => CreateProvider(_ => { }, StockItems);

    private static void StockItems(IGameDataService gameData)
    {
        gameData.GetItem(Potion).Returns(new ItemData
        {
            Num = Potion, Countable = 1, BuyPrice = PotionPrice, SellPrice = 1, Weight = 1, Race = 1, SellingGroup = SellingGroup,
        });
        gameData.GetItem(Arrow).Returns(new ItemData
        {
            Num = Arrow, Countable = 1, BuyPrice = ArrowPrice, SellPrice = 1, Weight = 1, Race = 1, SellingGroup = SellingGroup,
        });
    }

    private static (UserSession Session, IClient Client, NpcInstance Npc) Shopper(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 94001, accountId: 95001);
        session.Hp = 100;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Money = StartingGold;
        session.Stats = new DerivedStats { MaxWeight = 10_000 };

        var npc = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 5000,
            SellingGroup = SellingGroup,
            ZoneId = 1,
            X = 10,
            Z = 10,
            MaxHp = 1,
            Hp = 1,
            NpcType = 11,
        });
        return (session, client, npc);
    }

    private static void Stock(ItemSlot slot, int itemId, ushort count)
    {
        slot.ItemId = itemId;
        slot.Count = count;
    }

    private static ItemSlot Bag(UserSession session, byte pos) =>
        session.Inventory[InventoryConstants.InventoryStart + pos];
}
