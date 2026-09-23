using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class LotteryTests : GameTestBase
{
    private const int AliceId = 10;
    private const int BobId = 20;
    private const int GoldBarId = 379068000;
    private const int SilverBarId = 379067000;

    private static readonly LotteryEventData SampleLottery = new()
    {
        Id = 1,
        Name = "Moradon Grand Lottery",
        DurationMinutes = 15,
        UserLimit = 5,
        ReqItemId = InventoryConstants.ItemGold,
        ReqItemCount = 100_000,
        AutoStart = true,
    };

    private static readonly List<LotteryRewardData> SampleRewards =
    [
        new() { Id = 1, LotteryId = 1, Place = 1, ItemId = GoldBarId, Count = 1 },
        new() { Id = 2, LotteryId = 1, Place = 2, ItemId = SilverBarId, Count = 2 },
        new() { Id = 3, LotteryId = 1, Place = 3, ItemId = SilverBarId, Count = 1 },
        new() { Id = 4, LotteryId = 1, Place = 4, ItemId = InventoryConstants.ItemGold, Count = 500_000 },
    ];

    private static ServiceProvider Provider() => CreateProvider(
        db =>
        {
            db.Characters.AddRange(
                new Character { Id = AliceId, AccountId = 1, Name = "Alice" },
                new Character { Id = BobId, AccountId = 2, Name = "Bob" });
            db.LotteryEvents.Add(SampleLottery);
            db.LotteryRewards.AddRange(SampleRewards);
        },
        gameData =>
        {
            var dict = new Dictionary<int, LotteryEventData> { [SampleLottery.Id] = SampleLottery };
            gameData.LotteryEventTable.Returns(dict);
            gameData.LotteryRewardsByEvent.Returns(SampleRewards.ToLookup(r => r.LotteryId));
            gameData.GetItem(GoldBarId).Returns(new ItemData { Num = GoldBarId, Name = "Gold bar (+0)", Duration = 1 });
            gameData.GetItem(SilverBarId).Returns(new ItemData { Num = SilverBarId, Name = "Silver bar (+0)", Duration = 1 });
            gameData.GetItem(InventoryConstants.ItemGold).Returns(new ItemData { Num = InventoryConstants.ItemGold, Name = "Coin (+0)", Duration = 0 });
        });

    private static (UserSession Session, List<Packet> Sent) Online(ServiceProvider provider, int characterId, string name)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(p => sent.Add(p)), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var session = provider.GetRequiredService<SessionManager>().CreateSession(client, characterId, characterId);
        session.Name = name;
        session.Level = 50;
        session.Hp = 100;
        return (session, sent);
    }

    [Fact]
    public async Task StartAsync_InitializesActiveEventAndBroadcasting()
    {
        using var provider = Provider();
        var (alice, aliceSent) = Online(provider, AliceId, "Alice");
        var lottery = provider.GetRequiredService<ILotteryService>();

        await lottery.StartAsync(1);

        lottery.ActiveEvent.Should().NotBeNull();
        lottery.ActiveEvent!.EventData.Name.Should().Be("Moradon Grand Lottery");
        lottery.ActiveEvent.TotalTickets.Should().Be(0);

        aliceSent.Should().Contain(p => p.GetOpcode() == (byte)GameOpcodes.GS_LOTTERY);
    }

    [Fact]
    public async Task BuyTicketAsync_DeductsNoahAndIncrementsTicketCount()
    {
        using var provider = Provider();
        var (alice, _) = Online(provider, AliceId, "Alice");
        alice.Money = 500_000;
        var lottery = provider.GetRequiredService<ILotteryService>();
        await lottery.StartAsync(1);

        var (success, message, myTickets, totalTickets) = await lottery.BuyTicketAsync(alice);

        success.Should().BeTrue();
        alice.Money.Should().Be(400_000);
        myTickets.Should().Be(1);
        totalTickets.Should().Be(1);
        lottery.ActiveEvent!.TicketsFor(AliceId).Should().Be(1);
    }

    [Fact]
    public async Task BuyTicketAsync_FailsWhenInsufficientFunds()
    {
        using var provider = Provider();
        var (alice, _) = Online(provider, AliceId, "Alice");
        alice.Money = 50_000;
        var lottery = provider.GetRequiredService<ILotteryService>();
        await lottery.StartAsync(1);

        var (success, message, myTickets, totalTickets) = await lottery.BuyTicketAsync(alice);

        success.Should().BeFalse();
        alice.Money.Should().Be(50_000);
        myTickets.Should().Be(0);
        totalTickets.Should().Be(0);
    }

    [Fact]
    public async Task BuyTicketAsync_EnforcesUserLimitPerPlayer()
    {
        using var provider = Provider();
        var (alice, _) = Online(provider, AliceId, "Alice");
        var (bob, _) = Online(provider, BobId, "Bob");
        alice.Money = 1_000_000;
        bob.Money = 1_000_000;
        var lottery = provider.GetRequiredService<ILotteryService>();
        await lottery.StartAsync(1);

        // Alice buys 5 tickets (hits limit)
        for (int i = 0; i < 5; i++)
        {
            var res = await lottery.BuyTicketAsync(alice);
            res.Success.Should().BeTrue();
        }

        // Alice 6th ticket should fail
        var (failSuccess, _, failMyTickets, failTotal) = await lottery.BuyTicketAsync(alice);
        failSuccess.Should().BeFalse();
        failTotal.Should().Be(5);
        failMyTickets.Should().Be(5);

        // Bob should still be able to buy tickets because UserLimit is per-player
        var bobRes = await lottery.BuyTicketAsync(bob);
        bobRes.Success.Should().BeTrue();
        bobRes.MyTickets.Should().Be(1);
        bobRes.TotalTickets.Should().Be(6);
    }

    [Fact]
    public async Task CloseAsync_DrawsWinnersAndDeliversRewardsToMailbox()
    {
        using var provider = Provider();
        var (alice, _) = Online(provider, AliceId, "Alice");
        var (bob, _) = Online(provider, BobId, "Bob");
        alice.Money = 500_000;
        bob.Money = 500_000;

        var lottery = provider.GetRequiredService<ILotteryService>();
        await lottery.StartAsync(1);

        await lottery.BuyTicketAsync(alice);
        await lottery.BuyTicketAsync(bob);

        // Close and draw
        await lottery.CloseAsync(cancelWithoutWinners: false);

        lottery.ActiveEvent.Should().BeNull();

        // Check database mails table to verify winners received mail with attachment
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var mails = await db.Mails.Include(m => m.Attachments).ToListAsync();
        mails.Should().HaveCount(2); // Alice & Bob won 1st & 2nd place

        var firstMail = mails.FirstOrDefault(m => m.Subject.Contains("1st Prize"));
        firstMail.Should().NotBeNull();
        firstMail!.Attachments.Should().HaveCount(1);
        firstMail.Attachments[0].ItemId.Should().Be(GoldBarId);

        var secondMail = mails.FirstOrDefault(m => m.Subject.Contains("2nd Prize"));
        secondMail.Should().NotBeNull();
        secondMail!.Attachments.Should().HaveCount(1);
        secondMail.Attachments[0].ItemId.Should().Be(SilverBarId);
    }

    [Fact]
    public async Task CloseAsync_CancelledWithoutWinners_RefundsParticipantsViaMailbox()
    {
        using var provider = Provider();
        var (alice, _) = Online(provider, AliceId, "Alice");
        alice.Money = 500_000;

        var lottery = provider.GetRequiredService<ILotteryService>();
        await lottery.StartAsync(1);
        await lottery.BuyTicketAsync(alice);
        await lottery.BuyTicketAsync(alice);
        await lottery.BuyTicketAsync(alice); // 3 tickets = 300,000

        await lottery.CloseAsync(cancelWithoutWinners: true);

        lottery.ActiveEvent.Should().BeNull();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mails = await db.Mails.Include(m => m.Attachments).ToListAsync();
        mails.Should().HaveCount(1);
        var refundMail = mails[0];
        refundMail.Subject.Should().Contain("Refund");
        refundMail.Attachments.Should().HaveCount(1);
        refundMail.Attachments[0].Kind.Should().Be(MailAttachmentKind.Gold);
        refundMail.Attachments[0].Count.Should().Be(300_000);
    }

    [Fact]
    public void LotteryScheduleData_Matches_CorrectlyEvaluatesTime()
    {
        var dailySchedule = new LotteryScheduleData
        {
            LotteryId = 1,
            Day = null, // Every day
            Hour = 14,
            Minute = 30
        };

        var specificDaySchedule = new LotteryScheduleData
        {
            LotteryId = 1,
            Day = DayOfWeek.Sunday,
            Hour = 20,
            Minute = 0
        };

        // Daily schedule matches any day at 14:30
        dailySchedule.Matches(new DateTime(2026, 9, 22, 14, 30, 15)).Should().BeTrue();
        dailySchedule.Matches(new DateTime(2026, 9, 22, 14, 31, 0)).Should().BeFalse();
        dailySchedule.Matches(new DateTime(2026, 9, 22, 15, 30, 0)).Should().BeFalse();

        // Sunday schedule
        var sunday = new DateTime(2026, 9, 27, 20, 0, 10); // Sept 27, 2026 is Sunday
        var monday = new DateTime(2026, 9, 28, 20, 0, 10); // Sept 28, 2026 is Monday
        specificDaySchedule.Matches(sunday).Should().BeTrue();
        specificDaySchedule.Matches(monday).Should().BeFalse();
    }
}
