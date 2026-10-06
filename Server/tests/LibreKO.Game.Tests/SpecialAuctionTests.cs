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

public class SpecialAuctionTests : GameTestBase
{
    private const int Earrings = 1310610106;
    private const int Necklace = 1320510810;
    private const int Check = SpecialAuctionService.MythrilCheck;
    private const long Million = 1_000_000;
    private const long Billion = 1_000_000_000;
    private const byte Today = 1;
    private const byte Bid = 2;
    private const byte Collect = 3;
    private const byte MyInfo = 4;
    private const byte Notice = 5;
    private const byte Claim = 6;
    private const byte Log = 7;

    private sealed class Clock(DateTime start) : TimeProvider
    {
        public DateTime Now = start;
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }

    private sealed record Player(UserSession Session, IClient Client, List<Packet> Sent);

    private static readonly DateTime Evening = new(2026, 10, 10, 20, 0, 0, DateTimeKind.Utc);
    private static int Serial => SpecialAuctionSchedule.SerialOf(new DateOnly(2026, 10, 10));
    private static int Day => SpecialAuctionSchedule.DayOf(Serial);

    private static (ServiceProvider Provider, Clock Clock) Provider()
    {
        var clock = new Clock(Evening);
        var row = SpecialAuctionSchedule.Row(1, Day);
        var lots = new[]
        {
            new SpecialAuctionLotData { Row = row, Slot = 0, ItemId = Earrings, Count = 1, StartPrice = Million, Step = (int)Million },
            new SpecialAuctionLotData { Row = row, Slot = 1, ItemId = Necklace, Count = 1, StartPrice = Million, Step = (int)Million },
        };
        var provider = CreateProvider(_ => { }, gameData =>
        {
            gameData.SpecialAuctionLotsByRow.Returns(lots.ToLookup(l => l.Row));
            gameData.GetItem(Check).Returns(new ItemData { Num = Check, Name = "Mythril Check (1B)", Kind = 255, Slot = 15, Countable = 0, Duration = 1, ReqLevelMax = 100 });
            gameData.GetItem(Earrings).Returns(new ItemData { Num = Earrings, Name = "Akara's Elf Metal Earrings", Kind = 93, Slot = 7, Countable = 0, Duration = 1000, ReqLevelMax = 100 });
        }, settings => settings.SpecialAuctionGroup = 1, services => services.AddSingleton<TimeProvider>(clock));
        return (provider, clock);
    }

    private static Player Join(ServiceProvider provider, int id, string name, int money, int checks = 0)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = provider.GetRequiredService<SessionManager>();
        var session = sessions.CreateSession(client, characterId: id, accountId: id + 1000);
        session.Name = name;
        session.Level = 70;
        session.Nation = AccountNation.Karus;
        session.ZoneId = 21;
        session.Hp = 100;
        session.Money = money;
        for (var i = 0; i < checks; i++)
        {
            session.Inventory[InventoryConstants.SlotMax + i].ItemId = Check;
            session.Inventory[InventoryConstants.SlotMax + i].Count = 1;
            session.Inventory[InventoryConstants.SlotMax + i].Durability = 1;
        }
        sessions.Regions.AddToRegion(session);
        return new Player(session, client, sent);
    }

    private static async Task<Packet> Send(ServiceProvider provider, Player player, byte sub, Action<Packet>? body = null)
    {
        var packet = new Packet(GameOpcodes.GS_AUCTION);
        packet.WriteByte(sub);
        body?.Invoke(packet);
        packet.ResetOffset();
        await provider.GetRequiredService<ISpecialAuctionService>().HandleAsync(player.Client, packet);
        return Reply(player, sub);
    }

    private static Packet Reply(Player player, byte sub)
    {
        var reply = player.Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_AUCTION && p.GetBytes()[1] == sub);
        reply.ResetOffset();
        reply.ReadByte();
        return reply;
    }

    private static Task<Packet> PlaceBid(ServiceProvider provider, Player player, byte slot, int itemId, long coins, params byte[] checkSlots) =>
        Send(provider, player, Bid, p =>
        {
            p.WriteByte(slot);
            p.WriteInt(itemId);
            p.WriteShort(1);
            p.WriteByte((byte)checkSlots.Length);
            foreach (var bagSlot in checkSlots)
            {
                p.WriteInt(Check);
                p.WriteByte(bagSlot);
            }
            p.WriteUInt((uint)coins);
            p.WriteLong(coins + checkSlots.Length * Billion);
        });

    private static List<(int Channel, byte Day, byte Slot, int Item, short Count, int Serial, long Total, byte Status)> Rows(Packet reply)
    {
        reply.ReadShort().Should().Be(1);
        var rows = new List<(int, byte, byte, int, short, int, long, byte)>();
        int count = reply.ReadByte();
        for (var i = 0; i < count; i++)
        {
            var channel = reply.ReadInt();
            var day = reply.ReadByte();
            var slot = reply.ReadByte();
            var item = reply.ReadInt();
            var itemCount = reply.ReadShort();
            var serial = reply.ReadInt();
            reply.ReadByte();
            rows.Add((channel, day, slot, item, itemCount, serial, reply.ReadLong(), reply.ReadByte()));
        }
        return rows;
    }

    [Fact]
    public async Task TodayListsTheDaysLots()
    {
        var (provider, _) = Provider();
        using var _p = provider;
        var player = Join(provider, 100, "Bidder", 10_000_000);

        var reply = await Send(provider, player, Today);

        reply.ReadShort().Should().Be(1);
        reply.ReadInt().Should().Be(1);
        reply.ReadInt();
        reply.ReadByte();
        reply.ReadInt().Should().Be(2 * 3600 + 55 * 60);
        reply.ReadByte().Should().Be((byte)Day);
        reply.ReadShort().Should().Be(2);
        reply.ReadByte().Should().Be(0);
        reply.ReadInt().Should().Be(Earrings);
        reply.ReadLong().Should().Be(0);
        reply.ReadSByteString().Should().BeEmpty();
    }

    [Fact]
    public async Task ABidTakesTheCoinsAndTheChecksAndLeadsTheLot()
    {
        var (provider, _) = Provider();
        using var _p = provider;
        var player = Join(provider, 100, "Bidder", 10_000_000, checks: 1);

        (await PlaceBid(provider, player, 0, Earrings, 5 * Million, 0)).ReadShort().Should().Be(1);

        player.Session.Money.Should().Be(5_000_000);
        player.Session.Inventory[InventoryConstants.SlotMax].IsEmpty.Should().BeTrue();
        var today = await Send(provider, player, Today);
        today.ReadShort();
        today.ReadInt();
        today.ReadInt();
        today.ReadByte();
        today.ReadInt();
        today.ReadByte();
        today.ReadShort();
        today.ReadByte();
        today.ReadInt();
        today.ReadLong().Should().Be(Billion + 5 * Million);
        today.ReadSByteString().Should().Be("Bidder");
    }

    [Theory]
    [InlineData(1_000_000, 0, -4)]
    [InlineData(2_500_000, 0, -4)]
    [InlineData(20_000_000, 0, -4)]
    public async Task ABidMustBeatTheStepInWholeMillionsWithinTheGold(long coins, int checks, short expected)
    {
        var (provider, _) = Provider();
        using var _p = provider;
        var player = Join(provider, 100, "Bidder", 10_000_000, checks);

        (await PlaceBid(provider, player, 0, Earrings, coins)).ReadShort().Should().Be(expected);
        player.Session.Money.Should().Be(10_000_000);
    }

    [Fact]
    public async Task ACheckThatIsNotInTheBagIsRefused()
    {
        var (provider, _) = Provider();
        using var _p = provider;
        var player = Join(provider, 100, "Bidder", 10_000_000);

        (await PlaceBid(provider, player, 0, Earrings, 2 * Million, 3)).ReadShort().Should().Be(-2);
    }

    [Fact]
    public async Task ATotalThatDoesNotAddUpIsRefused()
    {
        var (provider, _) = Provider();
        using var _p = provider;
        var player = Join(provider, 100, "Bidder", 10_000_000);

        var reply = await Send(provider, player, Bid, p =>
        {
            p.WriteByte(0);
            p.WriteInt(Earrings);
            p.WriteShort(1);
            p.WriteByte(0);
            p.WriteUInt((uint)(2 * Million));
            p.WriteLong(3 * Million);
        });

        reply.ReadShort().Should().Be(-3);
    }

    [Fact]
    public async Task TheTopBidderCannotBidAgain()
    {
        var (provider, _) = Provider();
        using var _p = provider;
        var player = Join(provider, 100, "Bidder", 10_000_000);

        (await PlaceBid(provider, player, 0, Earrings, 2 * Million)).ReadShort().Should().Be(1);
        (await PlaceBid(provider, player, 0, Earrings, 4 * Million)).ReadShort().Should().Be(-7);
    }

    [Fact]
    public async Task AnOutbidPlayerCollectsTheBidBack()
    {
        var (provider, _) = Provider();
        using var _p = provider;
        var first = Join(provider, 100, "First", 10_000_000, checks: 1);
        var second = Join(provider, 200, "Second", 10_000_000, checks: 2);

        (await PlaceBid(provider, first, 0, Earrings, 2 * Million, 0)).ReadShort().Should().Be(1);
        (await PlaceBid(provider, second, 0, Earrings, 2 * Million, 0, 1)).ReadShort().Should().Be(1);

        var row = Rows(await Send(provider, first, MyInfo)).Single();
        row.Status.Should().Be((byte)AuctionBidStatus.Outbid);
        row.Total.Should().Be(Billion + 2 * Million);

        var collected = await Send(provider, first, Collect, p =>
        {
            p.WriteInt(row.Channel);
            p.WriteByte(row.Day);
            p.WriteByte(row.Slot);
            p.WriteInt(row.Item);
            p.WriteShort(row.Count);
            p.WriteInt(row.Serial);
            p.WriteByte(0);
        });

        collected.ReadShort().Should().Be(1);
        first.Session.Money.Should().Be(10_000_000);
        first.Session.Inventory.Count(s => s.ItemId == Check).Should().Be(1);
        Rows(await Send(provider, first, MyInfo)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheTopBidderWinsAtSettlementAndReceivesTheItem()
    {
        var (provider, clock) = Provider();
        using var _p = provider;
        var winner = Join(provider, 100, "Winner", 10_000_000);
        var watcher = Join(provider, 200, "Watcher", 0);

        (await PlaceBid(provider, winner, 0, Earrings, 3 * Million)).ReadShort().Should().Be(1);

        clock.Now = Evening.Date.AddHours(22).AddMinutes(56);
        (await PlaceBid(provider, watcher, 1, Necklace, 0)).ReadShort().Should().Be(-9);
        (await Send(provider, winner, MyInfo)).ReadShort().Should().Be(2);

        clock.Now = Evening.Date.AddHours(23).AddMinutes(1);
        var row = Rows(await Send(provider, winner, MyInfo)).Single();
        row.Status.Should().Be((byte)AuctionBidStatus.Won);
        var ended = Reply(watcher, Notice);
        ended.ReadByte().Should().Be(1);
        ended.ReadShort();
        ended.ReadByte();
        ended.ReadByte().Should().Be(2);
        ended.ReadInt().Should().Be(Earrings);
        ended.ReadLong().Should().Be(3 * Million);
        ended.ReadByte().Should().Be((byte)AuctionBidStatus.Won);

        var claim = await Send(provider, winner, Claim, p =>
        {
            p.WriteInt(row.Channel);
            p.WriteByte(row.Day);
            p.WriteByte(row.Slot);
            p.WriteInt(row.Item);
            p.WriteShort(row.Count);
        });

        claim.ReadShort().Should().Be(1);
        winner.Session.Inventory.Should().Contain(s => s.ItemId == Earrings);
        Rows(await Send(provider, winner, MyInfo)).Should().BeEmpty();

        var log = await Send(provider, winner, Log);
        log.ReadShort().Should().Be(1);
        log.ReadInt().Should().Be(Earrings);
        log.ReadLong().Should().Be(3 * Million);
        log.ReadByte().Should().Be((byte)AuctionBidStatus.Won);
        log.ReadInt().Should().Be(Necklace);
        log.ReadLong().Should().Be(0);
        log.ReadByte().Should().Be((byte)AuctionBidStatus.Cancelled);
    }

    [Fact]
    public async Task AnOpenBidKeepsTheNameFromChanging()
    {
        var (provider, _) = Provider();
        using var _p = provider;
        var player = Join(provider, 100, "Bidder", 10_000_000);

        (await provider.GetRequiredService<ISpecialAuctionService>().HasOpenAuctionAsync(100)).Should().BeFalse();
        (await PlaceBid(provider, player, 0, Earrings, 2 * Million)).ReadShort().Should().Be(1);
        (await provider.GetRequiredService<ISpecialAuctionService>().HasOpenAuctionAsync(100)).Should().BeTrue();

        var rename = new Packet(GameOpcodes.GS_NAME_CHANGE);
        rename.WriteByte(0);
        rename.WriteString("Renamed");
        rename.ResetOffset();
        await provider.GetRequiredService<IMiscPacketCoordinator>().HandleNameChangeAsync(player.Client, rename);

        var refusal = player.Sent.Last(p => p.GetOpcode() == (byte)GameOpcodes.GS_NAME_CHANGE);
        refusal.ResetOffset();
        refusal.ReadByte().Should().Be(11);
        player.Session.Name.Should().Be("Bidder");
    }
}
