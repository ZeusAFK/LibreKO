using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MerchantSearchTests : GameTestBase
{
    private const int OfficialList = 810166000;
    private const byte OfficialListSub = 0x30;
    private const byte Move = 2;
    private const byte Open = 5;
    private const byte Results = 6;
    private const byte LastChunk = 3;
    private const byte Moradon = 21;
    private const int Sword = 120010000;
    private const int Potion = 389010000;

    private static ServiceProvider Provider() => CreateProvider(_ => { }, gameData =>
        gameData.GetItem(OfficialList).Returns(new ItemData
        {
            Num = OfficialList, Name = "Menissiah's official list", Kind = 255, Slot = 15, Duration = 1, ReqLevelMax = 100,
        }));

    private static (UserSession Session, List<Packet> Sent, IClient Client) Player(
        ServiceProvider provider, int id, string name, float x, float z, bool carriesList = false)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: id, accountId: id + 1000);
        session.Name = name;
        session.Level = 60;
        session.Class = 201;
        session.Nation = AccountNation.ElMorad;
        session.ZoneId = Moradon;
        session.X = x;
        session.Z = z;
        session.Hp = 100;
        session.MaxHp = 100;
        if (carriesList)
        {
            session.Inventory[InventoryConstants.SlotMax].ItemId = OfficialList;
            session.Inventory[InventoryConstants.SlotMax].Count = 1;
            session.Inventory[InventoryConstants.SlotMax].Durability = 1;
        }
        sessions.Regions.AddToRegion(session);
        return (session, sent, client);
    }

    private static void Sells(UserSession merchant, int slot, int itemId, int price)
    {
        merchant.Trade.MerchantState = MerchantMode.Selling;
        merchant.Trade.MerchantItems[slot] = new MerchantItem { ItemId = itemId, Price = price, Count = 1 };
    }

    private static async Task Send(ServiceProvider provider, IClient client, params object[] fields)
    {
        var packet = new Packet(GameOpcodes.GS_MERCHANT);
        packet.WriteByte(OfficialListSub);
        foreach (var field in fields)
        {
            switch (field)
            {
                case byte b: packet.WriteByte(b); break;
                case ushort s: packet.WriteShort((short)s); break;
                case int i: packet.WriteInt(i); break;
            }
        }
        packet.ResetOffset();
        await provider.GetRequiredService<IMerchantPacketCoordinator>().HandleAsync(client, packet);
    }

    private static Packet Reply(List<Packet> sent, byte sub)
    {
        var packet = sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_MERCHANT && p.GetBytes().Length > 2
                                    && p.GetBytes()[1] == OfficialListSub && p.GetBytes()[2] == sub);
        packet.ResetOffset();
        packet.ReadByte().Should().Be(OfficialListSub);
        packet.ReadByte().Should().Be(sub);
        return packet;
    }

    [Fact]
    public async Task TheListOpensTheWindowForItsOwner()
    {
        using var provider = Provider();
        var (_, sent, client) = Player(provider, 100, "Seeker", 800, 400, carriesList: true);

        await Send(provider, client, Open, OfficialList);

        Reply(sent, Open).ReadByte().Should().Be(1);
    }

    [Fact]
    public async Task WithoutTheListTheWindowStaysShut()
    {
        using var provider = Provider();
        var (_, sent, client) = Player(provider, 100, "Seeker", 800, 400);

        await Send(provider, client, Open, OfficialList);

        Reply(sent, Open).ReadByte().Should().Be(0);
    }

    [Fact]
    public async Task EveryStallInTheZoneIsListedWithItsTwelveSlots()
    {
        using var provider = Provider();
        var (_, sent, client) = Player(provider, 100, "Seeker", 800, 400, carriesList: true);
        var (seller, _, _) = Player(provider, 200, "Seller", 820, 410);
        Sells(seller, 0, Sword, 5000);
        Sells(seller, 3, Potion, 70);
        var (buyer, _, _) = Player(provider, 300, "Buyer", 830, 420);
        buyer.Trade.MerchantState = MerchantMode.Buying;
        buyer.Trade.BuyMerchantItems[1] = new MerchantItem { ItemId = Potion, Price = 60, Count = 10 };
        var (elsewhere, _, _) = Player(provider, 400, "Elsewhere", 100, 100);
        elsewhere.ZoneId = 1;
        Sells(elsewhere, 0, Sword, 1);

        await Send(provider, client, Results, 0);

        var packet = Reply(sent, Results);
        packet.ReadByte().Should().Be(LastChunk);
        packet.ReadInt().Should().Be(2);
        packet.ReadShort().Should().Be(2);
        packet.ReadInt().Should().Be(200);
        packet.ReadByte().Should().Be(6);
        System.Text.Encoding.ASCII.GetString(Enumerable.Range(0, 6).Select(_ => packet.ReadByte()).ToArray()).Should().Be("Seller");
        packet.ReadByte().Should().Be(0);
        packet.ReadInt().Should().Be(Sword);
        packet.ReadInt().Should().Be(5000);
        for (var slot = 1; slot < 3; slot++)
        {
            packet.ReadInt().Should().Be(0);
            packet.ReadInt().Should().Be(0);
        }
        packet.ReadInt().Should().Be(Potion);
        packet.ReadInt().Should().Be(70);
        for (var slot = 4; slot < TradeState.MerchantSlots; slot++)
        {
            packet.ReadInt();
            packet.ReadInt();
        }
        packet.ReadInt().Should().Be(300);
        packet.ReadByte().Should().Be(5);
        System.Text.Encoding.ASCII.GetString(Enumerable.Range(0, 5).Select(_ => packet.ReadByte()).ToArray()).Should().Be("Buyer");
        packet.ReadByte().Should().Be(1);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(0);
        packet.ReadInt().Should().Be(Potion);
        packet.ReadInt().Should().Be(60);
    }

    [Fact]
    public async Task MoveBringsTheSeekerBesideTheStall()
    {
        using var provider = Provider();
        var (seeker, sent, client) = Player(provider, 100, "Seeker", 800, 400, carriesList: true);
        var (seller, _, _) = Player(provider, 200, "Seller", 850, 450);
        Sells(seller, 0, Sword, 5000);

        await Send(provider, client, Move, (ushort)200);

        Reply(sent, Move).ReadByte().Should().Be(1);
        Math.Abs(seeker.X - seller.X).Should().BeLessThan(3f);
        Math.Abs(seeker.Z - seller.Z).Should().BeLessThan(3f);
    }

    [Fact]
    public async Task MoveToSomeoneWhoIsNotSellingFails()
    {
        using var provider = Provider();
        var (seeker, sent, client) = Player(provider, 100, "Seeker", 800, 400, carriesList: true);
        Player(provider, 200, "Walker", 850, 450);

        await Send(provider, client, Move, (ushort)200);

        Reply(sent, Move).ReadByte().Should().Be(0);
        seeker.X.Should().Be(800);
    }
}
