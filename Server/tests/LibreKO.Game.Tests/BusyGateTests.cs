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

// While a player trades, keeps or sets up a stall, gathers or lies dead, nothing moves an
// item out of their bag: a trade or a stall may be counting on it.
public class BusyGateTests : GameTestBase
{
    // None is the control: the same request from an idle player goes through, so a busy
    // player's refusal is down to being busy and nothing else.
    public enum Busy { None, Trading, Selling, Preparing, Gathering, Dead }

    private const int CharacterId = 93001;
    private const int Ware = 156_210_000;
    private const int Other = 156_220_000;
    private const byte WarePos = 0;
    private const byte OtherPos = 1;
    private const int ShopNpcId = 5000;
    private const int SellingGroup = 1;
    private const string RecipientName = "Recipient";

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Trading)]
    [InlineData(Busy.Selling)]
    [InlineData(Busy.Preparing)]
    [InlineData(Busy.Dead)]
    public async Task OnlyAnIdlePlayerDestroysAnItem(Busy state)
    {
        using var provider = Provider();
        var (session, client, _) = Player(provider, state);

        var packet = new Packet(GameOpcodes.GS_ITEM_REMOVE);
        packet.WriteByte(0);
        packet.WriteByte(WarePos);
        packet.WriteInt(Ware);
        await provider.GetRequiredService<IItemRemoveService>().HandleAsync(client, Reset(packet));

        Bag(session, WarePos).ItemId.Should().Be(state == Busy.None ? 0 : Ware);
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Trading)]
    [InlineData(Busy.Selling)]
    [InlineData(Busy.Preparing)]
    [InlineData(Busy.Gathering)]
    public async Task OnlyAnIdlePlayerSwapsBagSlots(Busy state)
    {
        using var provider = Provider();
        var (session, client, _) = Player(provider, state);

        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(3);
        packet.WriteInt(Ware);
        packet.WriteByte(WarePos);
        packet.WriteByte(OtherPos);
        await provider.GetRequiredService<IItemTradeService>().HandleTradeAsync(client, Reset(packet));

        var idle = state == Busy.None;
        Bag(session, WarePos).ItemId.Should().Be(idle ? Other : Ware, "a listed or offered slot must keep its item");
        Bag(session, OtherPos).ItemId.Should().Be(idle ? Ware : Other);
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Trading)]
    [InlineData(Busy.Selling)]
    [InlineData(Busy.Preparing)]
    [InlineData(Busy.Gathering)]
    public async Task OnlyAnIdlePlayerDropsAnItem(Busy state)
    {
        using var provider = Provider();
        var (session, client, _) = Player(provider, state);

        var packet = new Packet(GameOpcodes.GS_ITEM_DROP);
        packet.WriteByte(WarePos);
        packet.WriteInt(Ware);
        packet.WriteUShort(1);
        await provider.GetRequiredService<ILootPacketCoordinator>().HandleItemDropAsync(client, Reset(packet));

        Bag(session, WarePos).ItemId.Should().Be(state == Busy.None ? 0 : Ware);
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Selling)]
    [InlineData(Busy.Preparing)]
    [InlineData(Busy.Gathering)]
    public async Task OnlyAnIdlePlayerSellsToAnNpc(Busy state)
    {
        using var provider = Provider();
        var (session, client, _) = Player(provider, state);
        var npc = provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            NpcId = ShopNpcId,
            SellingGroup = SellingGroup,
            ZoneId = session.ZoneId,
            X = session.X,
            Z = session.Z,
            MaxHp = 1,
            Hp = 1,
            NpcType = 11,
        });

        var packet = new Packet(GameOpcodes.GS_ITEM_TRADE);
        packet.WriteByte(2);
        packet.WriteInt(SellingGroup);
        packet.WriteInt(npc.UniqueId);
        packet.WriteByte(1);
        packet.WriteInt(Ware);
        packet.WriteByte(WarePos);
        packet.WriteUShort(1);
        await provider.GetRequiredService<IItemTradeService>().HandleTradeAsync(client, Reset(packet));

        var idle = state == Busy.None;
        Bag(session, WarePos).ItemId.Should().Be(idle ? 0 : Ware);
        (session.Money > 0).Should().Be(idle);
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Trading)]
    [InlineData(Busy.Selling)]
    [InlineData(Busy.Preparing)]
    [InlineData(Busy.Gathering)]
    [InlineData(Busy.Dead)]
    public async Task OnlyAnIdlePlayerStoresAnItem(Busy state)
    {
        using var provider = Provider();
        var (session, client, _) = Player(provider, state);

        await SendWarehouseAsync(provider, client, WarehouseSubOpcode.Input, Ware, WarePos, 0, count: 1);

        var idle = state == Busy.None;
        Bag(session, WarePos).ItemId.Should().Be(idle ? 0 : Ware);
        session.Warehouse[0].ItemId.Should().Be(idle ? Ware : 0);
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Trading)]
    public async Task OnlyAnIdlePlayerTakesAnItemOutOfStorage(Busy state)
    {
        using var provider = Provider();
        var (session, client, _) = Player(provider, state);
        session.Warehouse[0].ItemId = Other;
        session.Warehouse[0].Count = 1;
        session.Warehouse[0].Durability = 50;
        Bag(session, OtherPos).Clear();

        await SendWarehouseAsync(provider, client, WarehouseSubOpcode.Output, Other, 0, OtherPos, count: 1);

        var idle = state == Busy.None;
        session.Warehouse[0].ItemId.Should().Be(idle ? 0 : Other);
        Bag(session, OtherPos).ItemId.Should().Be(idle ? Other : 0);
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Selling)]
    public async Task OnlyAnIdlePlayerRearrangesTheBagThroughStorage(Busy state)
    {
        using var provider = Provider();
        var (session, client, _) = Player(provider, state);
        Bag(session, OtherPos).Clear();

        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte((byte)WarehouseSubOpcode.InventoryMove);
        packet.WriteInt(0);
        packet.WriteInt(Ware);
        packet.WriteByte(0);
        packet.WriteByte(WarePos);
        packet.WriteByte(OtherPos);
        await provider.GetRequiredService<IWarehousePacketCoordinator>().HandleAsync(client, Reset(packet));

        var idle = state == Busy.None;
        Bag(session, WarePos).ItemId.Should().Be(idle ? 0 : Ware);
        Bag(session, OtherPos).ItemId.Should().Be(idle ? Ware : 0);
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Preparing)]
    [InlineData(Busy.Dead)]
    public async Task OnlyAnIdlePlayerMovesOrArrangesTheBag(Busy state)
    {
        using var provider = Provider();
        var (session, client, _) = Player(provider, state);
        Bag(session, OtherPos).Clear();
        var idle = state == Busy.None;

        var move = new Packet(GameOpcodes.GS_ITEM_MOVE);
        move.WriteByte(1);
        move.WriteByte((byte)ItemMoveDirection.InventoryToInventory);
        move.WriteInt(Ware);
        move.WriteByte(WarePos);
        move.WriteByte(OtherPos);
        await provider.GetRequiredService<IItemPacketCoordinator>().HandleMoveAsync(client, Reset(move));

        Bag(session, WarePos).ItemId.Should().Be(idle ? 0 : Ware);
        Bag(session, OtherPos).ItemId.Should().Be(idle ? Ware : 0);

        Bag(session, 5).ItemId = Other;
        Bag(session, 5).Count = 1;
        var arrange = new Packet(GameOpcodes.GS_ITEM_MOVE);
        arrange.WriteByte(2);
        await provider.GetRequiredService<IItemPacketCoordinator>().HandleMoveAsync(client, Reset(arrange));

        Bag(session, 5).ItemId.Should().Be(idle ? 0 : Other, "arranging would move the item a listing points at");
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Trading)]
    [InlineData(Busy.Selling)]
    [InlineData(Busy.Preparing)]
    [InlineData(Busy.Gathering)]
    [InlineData(Busy.Dead)]
    public async Task OnlyAnIdlePlayerMailsAnItem(Busy state)
    {
        using var provider = Provider(seedRecipient: true);
        var (session, _, _) = Player(provider, state);

        await provider.GetRequiredService<IMailService>().SendAsync(session, RecipientName, "Subject", "Body", 0,
            [new MailItemPick((byte)(InventoryConstants.InventoryStart + WarePos), 1)]);

        var idle = state == Busy.None;
        Bag(session, WarePos).ItemId.Should().Be(idle ? 0 : Ware);
        await using var scope = provider.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Mails.CountAsync()).Should().Be(idle ? 1 : 0);
    }

    [Theory]
    [InlineData(Busy.None)]
    [InlineData(Busy.Trading)]
    [InlineData(Busy.Selling)]
    [InlineData(Busy.Preparing)]
    [InlineData(Busy.Gathering)]
    [InlineData(Busy.Dead)]
    public async Task OnlyAnIdlePlayerSendsAnItemByLetter(Busy state)
    {
        using var provider = Provider(seedRecipient: true);
        var (session, client, _) = Player(provider, state);
        session.Money = 20_000;

        var packet = new Packet(GameOpcodes.GS_SHOPPING_MALL);
        packet.WriteByte(6);
        packet.WriteByte(6);
        packet.WriteSByteString(RecipientName);
        packet.WriteSByteString("Subject");
        packet.WriteByte(2);
        packet.WriteInt(Ware);
        packet.WriteByte(WarePos);
        packet.WriteInt(0);
        packet.WriteString("Message");
        await provider.GetRequiredService<IShoppingMallPacketCoordinator>().HandleAsync(client, Reset(packet));

        var idle = state == Busy.None;
        Bag(session, WarePos).ItemId.Should().Be(idle ? 0 : Ware);
        (session.Money < 20_000).Should().Be(idle);
    }

    private static async Task SendWarehouseAsync(
        ServiceProvider provider, IClient client, WarehouseSubOpcode sub, int itemId, byte src, byte dst, int count)
    {
        var packet = new Packet(GameOpcodes.GS_WAREHOUSE);
        packet.WriteByte((byte)sub);
        packet.WriteInt(0);
        packet.WriteInt(itemId);
        packet.WriteByte(0);
        packet.WriteByte(src);
        packet.WriteByte(dst);
        packet.WriteInt(count);
        await provider.GetRequiredService<IWarehousePacketCoordinator>().HandleAsync(client, Reset(packet));
    }

    private static ServiceProvider Provider(bool seedRecipient = false) => CreateProvider(
        db =>
        {
            if (!seedRecipient)
                return;
            db.Characters.Add(new Character
            {
                AccountId = 1,
                Slot = 0,
                Name = RecipientName,
                Race = 1,
                Class = 101,
                Face = 1,
                Hair = 1,
                Level = 10,
                Hp = 100,
                Mp = 100,
                MapId = 1,
                Items = new byte[InventoryConstants.InventoryTotal * 8],
                SkillPointData = new byte[9],
            });
        },
        StockItems);

    private static void StockItems(IGameDataService gameData)
    {
        gameData.GetItem(Ware).Returns(new ItemData
        {
            Num = Ware, Race = 1, Countable = 0, Duration = 50, BuyPrice = 600, SellingGroup = SellingGroup,
        });
        gameData.GetItem(Other).Returns(new ItemData { Num = Other, Race = 1, Countable = 0, Duration = 50 });
    }

    private static (UserSession Session, IClient Client, List<Packet> Sent) Player(ServiceProvider provider, Busy state)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, CharacterId, CharacterId + 1000);
        session.Name = "Busy";
        session.Hp = 100;
        session.ZoneId = 1;
        session.X = 10;
        session.Z = 10;
        session.Stats = new DerivedStats { MaxWeight = 10_000 };
        Stock(Bag(session, WarePos), Ware);
        Stock(Bag(session, OtherPos), Other);

        switch (state)
        {
            case Busy.Trading:
                session.Trade.ExchangeUser = CharacterId + 1;
                break;
            case Busy.Selling:
                session.Trade.MerchantState = MerchantMode.Selling;
                session.Trade.MerchantItems[0] = new MerchantItem
                {
                    ItemId = Ware, Count = 1, Price = 100, Durability = 50,
                    OriginalSlot = InventoryConstants.InventoryStart + WarePos,
                };
                break;
            case Busy.Preparing:
                session.Trade.IsSellingMerchantPreparing = true;
                break;
            case Busy.Gathering:
                session.IsMining = true;
                break;
            case Busy.Dead:
                session.Hp = 0;
                break;
        }

        return (session, client, sent);
    }

    private static void Stock(ItemSlot slot, int itemId)
    {
        slot.ItemId = itemId;
        slot.Count = 1;
        slot.Durability = 50;
    }

    private static ItemSlot Bag(UserSession session, byte pos) =>
        session.Inventory[InventoryConstants.InventoryStart + pos];

    private static Packet Reset(Packet packet)
    {
        packet.ResetOffset();
        return packet;
    }
}
