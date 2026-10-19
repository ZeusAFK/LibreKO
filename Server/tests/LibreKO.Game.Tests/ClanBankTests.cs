using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class ClanBankTests : GameTestBase
{
    private const short Wolves = 71;
    private const int StoredItem = 156_210_000;
    private const byte Member = 3;
    private const byte Chief = 1;
    private const short AliveHp = 100;
    private const byte PageSize = 24;

    [Fact]
    public async Task SomeoneWithoutAClanIsToldSo()
    {
        var bank = Bank(clan: 0, fame: 0);

        await bank.Send(WarehouseSubOpcode.Open, _ => { });

        bank.LastResult(WarehouseSubOpcode.Open).Should().Be((byte)ClanWarehouseResult.NotInClan);
    }

    [Fact]
    public async Task AMemberCannotTakeOut()
    {
        var bank = Bank(Wolves, Member);
        bank.Slots()[0].ItemId = StoredItem;
        bank.Slots()[0].Count = 1;

        await bank.Send(WarehouseSubOpcode.Output, packet =>
        {
            packet.WriteInt(0);
            packet.WriteInt(StoredItem);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteInt(1);
        });

        bank.LastResult(WarehouseSubOpcode.Output).Should().Be((byte)ClanWarehouseResult.NoRights);
        bank.Slots()[0].ItemId.Should().Be(StoredItem);
    }

    [Fact]
    public async Task AnUntradableItemIsNamedNotStorable()
    {
        var bank = Bank(Wolves, Member);
        var bag = bank.Session.Inventory[InventoryConstants.SlotMax];
        bag.ItemId = StoredItem;
        bag.Count = 1;
        bag.Flag = (byte)ItemFlag.Bound;

        await bank.Send(WarehouseSubOpcode.Input, packet =>
        {
            packet.WriteInt(0);
            packet.WriteInt(StoredItem);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteInt(1);
        });

        bank.LastResult(WarehouseSubOpcode.Input).Should().Be((byte)ClanWarehouseResult.NotStorable);
        bank.Session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(StoredItem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyAMemberWhoIsNotTradingStoresAnItem(bool trading)
    {
        var bank = Bank(Wolves, Member);
        var bag = bank.Session.Inventory[InventoryConstants.SlotMax];
        bag.ItemId = StoredItem;
        bag.Count = 1;
        bag.Durability = 50;
        if (trading)
            bank.Session.Trade.ExchangeUser = 9_999;

        await bank.Send(WarehouseSubOpcode.Input, packet =>
        {
            packet.WriteInt(0);
            packet.WriteInt(StoredItem);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteByte(0);
            packet.WriteInt(1);
        });

        bank.LastResult(WarehouseSubOpcode.Input).Should().Be((byte)(trading ? ClanWarehouseResult.Failed : ClanWarehouseResult.Succeeded));
        bank.Session.Inventory[InventoryConstants.SlotMax].ItemId.Should().Be(trading ? StoredItem : 0);
        bank.Slots()[0].ItemId.Should().Be(trading ? 0 : StoredItem);
    }

    [Fact]
    public async Task TheChiefMovesAnItemToAnotherPage()
    {
        var bank = Bank(Wolves, Chief);
        bank.Slots()[1].ItemId = StoredItem;
        bank.Slots()[1].Count = 1;

        await bank.Send(WarehouseSubOpcode.Move, packet =>
        {
            packet.WriteInt(0);
            packet.WriteInt(StoredItem);
            packet.WriteByte(0);
            packet.WriteByte(1);
            packet.WriteByte(5);
            packet.WriteByte(2);
        });

        bank.LastResult(WarehouseSubOpcode.Move).Should().Be((byte)ClanWarehouseResult.Succeeded);
        bank.Slots()[2 * PageSize + 5].ItemId.Should().Be(StoredItem);
        bank.Slots()[1].IsEmpty.Should().BeTrue();
    }

    private static BankHarness Bank(short clan, byte fame)
    {
        var provider = CreateProvider(db =>
        {
            if (clan <= 0) return;
            db.Knights.Add(new KnightsEntity { Id = clan, Name = "Wolves", Chief = "Alpha", Nation = 1 });
            db.SaveChanges();
        }, data => data.GetItem(StoredItem)
            .Returns(new ItemData { Num = StoredItem, Countable = 0, Duration = 50 }));
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(packet => sent.Add(packet)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        if (clan > 0) sessions.Knights.AddClan(clan, new KnightsEntity { Id = clan, Name = "Wolves", Chief = "Alpha", Nation = 1 });
        var session = sessions.CreateSession(client, characterId: 71, accountId: 81);
        session.Hp = AliveHp;
        session.KnightsId = clan;
        session.KnightsFame = fame;
        return new BankHarness(provider, client, session, sent, clan);
    }

    private sealed record BankHarness(ServiceProvider Provider, IClient Client, UserSession Session, List<Packet> Sent, short Clan)
    {
        public ItemSlot[] Slots() => Provider.GetRequiredService<SessionManager>().Knights.GetClanWarehouse(Clan)!;

        public async Task Send(WarehouseSubOpcode sub, Action<Packet> body)
        {
            var packet = new Packet(GameOpcodes.GS_CLAN_WAREHOUSE);
            packet.WriteByte((byte)sub);
            body(packet);
            await Provider.GetRequiredService<IClanWarehousePacketCoordinator>().HandleAsync(Client, packet);
        }

        public byte LastResult(WarehouseSubOpcode sub)
        {
            var reply = Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_CLAN_WAREHOUSE && SubOf(p) == (byte)sub);
            reply.ResetOffset();
            reply.ReadByte();
            return reply.ReadByte();
        }

        private static byte SubOf(Packet packet)
        {
            packet.ResetOffset();
            return packet.ReadByte();
        }
    }
}
