using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class EventPostTests
{
    private const string Phrase = "I Love Knight Online";
    private const string ServerName = "Destan";
    private const int CharacterId = 7;
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class Board : IDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required UserSession Session { get; init; }
        public required List<Packet> Sent { get; init; }
        public required EventPostPacketCoordinator Coordinator { get; init; }
        public required FakeTimeProvider Clock { get; init; }

        public Task SendAsync(params object[] fields)
        {
            var packet = new Packet(GameOpcodes.GS_EVENT_BOARD);
            foreach (var field in fields)
            {
                switch (field)
                {
                    case EventBoardSubOpcode sub: packet.WriteByte((byte)sub); break;
                    case uint value: packet.WriteUInt(value); break;
                    case string text: packet.WriteSByteString(text); break;
                }
            }
            packet.ResetOffset();
            return Coordinator.HandleAsync(Session.Client, packet);
        }

        public Task PostAsync(string text = Phrase, uint eventId = EventPostRules.LoveKnightOnline) =>
            SendAsync(EventBoardSubOpcode.PostRegister, eventId, text);

        public Packet Last(EventBoardSubOpcode sub)
        {
            var packet = Sent.Last(sent => sent.GetOpcode() == (byte)GameOpcodes.GS_EVENT_BOARD && Peek(sent) == (byte)sub);
            packet.ResetOffset();
            packet.ReadByte();
            return packet;
        }

        public uint Result()
        {
            var reply = Last(EventBoardSubOpcode.PostRegister);
            reply.ReadUInt();
            return reply.ReadUInt();
        }

        public int Count(int itemId) => Session.Inventory.Count(slot => slot.ItemId == itemId);

        public List<EventPost> Stored()
        {
            using var scope = Provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().EventPosts.ToList();
        }

        private static byte Peek(Packet packet)
        {
            packet.ResetOffset();
            return packet.ReadByte();
        }

        public void Dispose() => Provider.Dispose();
    }

    private static Board Open(DateTimeOffset now)
    {
        var services = new ServiceCollection();
        var database = $"EventPosts_{Guid.NewGuid():N}";
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(database)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IEventPostRepository, EventPostRepository>();
        var provider = services.BuildServiceProvider();

        var sent = new List<Packet>();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, CharacterId, 1);
        session.Name = "Rin";

        var gameData = Substitute.For<IGameDataService>();
        gameData.GetItem(Arg.Any<int>()).Returns(call => new ItemData { Num = call.Arg<int>(), Duration = 1 });
        var servers = Substitute.For<IServerRepository>();
        servers.GetServers().Returns([new Server { Id = 1, Name = ServerName }]);
        var clock = new FakeTimeProvider(now);

        var coordinator = new EventPostPacketCoordinator(
            sessions, provider.GetRequiredService<IServiceScopeFactory>(), servers,
            new ItemGrantService(gameData, Substitute.For<IUserNotificationService>()), gameData,
            Options.Create(new GameServerSettings { ServerId = 1 }),
            NullLogger<EventPostPacketCoordinator>.Instance, clock);
        return new Board { Provider = provider, Session = session, Sent = sent, Coordinator = coordinator, Clock = clock };
    }

    private static DateTimeOffset Noon(int day) => new(2026, 11, day, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ThePhraseMustMatchItsEventIgnoringCaseAndOuterSpaces()
    {
        EventPostRules.Matches(EventPostRules.LoveKnightOnline, "  i love knight online ").Should().BeTrue();
        EventPostRules.Matches(EventPostRules.ContinuePlaying, "I Will Continue Playing Knight Online").Should().BeTrue();
        EventPostRules.Matches(EventPostRules.LoveKnightOnline, "I Will Continue Playing Knight Online").Should().BeFalse();
        EventPostRules.Matches(EventPostRules.LoveKnightOnline, "I Love Knight").Should().BeFalse();
    }

    [Theory]
    [InlineData(1, new[] { EventPostRules.DailyVoucher })]
    [InlineData(3, new[] { EventPostRules.DailyVoucher, EventPostRules.ThreeDayVoucher })]
    [InlineData(7, new[] { EventPostRules.DailyVoucher, EventPostRules.SevenDayVoucher })]
    [InlineData(15, new[] { EventPostRules.DailyVoucher, EventPostRules.FifteenDayVoucher })]
    [InlineData(30, new[] { EventPostRules.DailyVoucher, EventPostRules.ThirtyDayVoucher, EventPostRules.DecorativeVoucher })]
    [InlineData(31, new[] { EventPostRules.DailyVoucher })]
    public void EveryPostPaysTheDailyVoucherAndTheMilestonesPayMore(int postsInPeriod, int[] rewards)
    {
        EventPostRules.Rewards(postsInPeriod).Should().Equal(rewards);
    }

    [Fact]
    public void TheDayAndThePeriodFollowTheEventClockNotUtc()
    {
        var calendar = EventPostRules.Calendar(new DateTime(2026, 11, 30, 23, 30, 0, DateTimeKind.Utc), Berlin);

        calendar.DayStart.Should().Be(new DateTime(2026, 11, 30, 23, 0, 0));
        calendar.PeriodStart.Should().Be(new DateTime(2026, 11, 30, 23, 0, 0));
        calendar.PeriodEnd.Should().Be(new DateTime(2026, 12, 31, 23, 0, 0));
    }

    [Fact]
    public async Task APostPaysTheDailyVoucherAndShowsOnTheBoard()
    {
        using var board = Open(Noon(10));

        await board.PostAsync();

        var reply = board.Last(EventBoardSubOpcode.PostRegister);
        reply.ReadUInt().Should().Be(EventPostRules.LoveKnightOnline);
        reply.ReadUInt().Should().Be(EventPostPacketWriter.Accepted);
        reply.ReadUInt().Should().Be(1u);
        reply.ReadUInt().Should().Be(1u);
        reply.ReadUInt();
        reply.ReadUInt();
        reply.ReadShort().Should().Be(1);
        reply.ReadSByteString().Should().Be(ServerName);
        reply.ReadSByteString().Should().Be("Rin");
        reply.ReadSByteString().Should().Be(Phrase);
        reply.ReadUInt().Should().Be((uint)Noon(10).ToUnixTimeSeconds());
        reply.RemainingBytes.Should().Be(0);
        board.Count(EventPostRules.DailyVoucher).Should().Be(1);
    }

    [Fact]
    public async Task ASecondPostTheSameDayIsRefusedAndPaysNothing()
    {
        using var board = Open(Noon(10));
        await board.PostAsync();

        await board.PostAsync();

        var reply = board.Last(EventBoardSubOpcode.PostRegister);
        reply.ReadUInt();
        reply.ReadUInt().Should().Be(EventPostPacketWriter.AlreadyPosted);
        board.Count(EventPostRules.DailyVoucher).Should().Be(1);
        board.Stored().Should().ContainSingle();
    }

    [Fact]
    public async Task TheOtherEventCountsSeparately()
    {
        using var board = Open(Noon(10));
        await board.PostAsync();

        await board.PostAsync("I Will Continue Playing Knight Online", EventPostRules.ContinuePlaying);

        board.Result().Should().Be(EventPostPacketWriter.Accepted);
        board.Count(EventPostRules.DailyVoucher).Should().Be(2);
    }

    [Fact]
    public async Task TheThirdPostOfTheMonthAlsoPaysTheThreeDayVoucher()
    {
        using var board = Open(Noon(10));
        await board.PostAsync();
        board.Clock.Advance(TimeSpan.FromDays(1));
        await board.PostAsync();
        board.Clock.Advance(TimeSpan.FromDays(1));

        await board.PostAsync();

        board.Count(EventPostRules.DailyVoucher).Should().Be(3);
        board.Count(EventPostRules.ThreeDayVoucher).Should().Be(1);
    }

    [Fact]
    public async Task AWrongPhraseIsRefusedWithItsOwnResult()
    {
        using var board = Open(Noon(10));

        await board.PostAsync("I Love Knight");

        board.Result().Should().Be(EventPostPacketWriter.WrongText);
        board.Stored().Should().BeEmpty();
    }

    [Fact]
    public async Task AFullBagRefusesThePostWithoutStoringIt()
    {
        using var board = Open(Noon(10));
        for (var index = InventoryConstants.SlotMax; index < InventoryConstants.SlotMax + InventoryConstants.HaveMax; index++)
            board.Session.Inventory[index].ItemId = 389010000;

        await board.PostAsync();

        board.Result().Should().Be(EventPostPacketWriter.Failed);
        board.Stored().Should().BeEmpty();
    }

    [Fact]
    public async Task TheHistoryListsThePlayersOwnPostsNewestFirst()
    {
        using var board = Open(Noon(10));
        await board.PostAsync();
        board.Clock.Advance(TimeSpan.FromDays(1));
        await board.PostAsync("I Will Continue Playing Knight Online", EventPostRules.ContinuePlaying);

        await board.SendAsync(EventBoardSubOpcode.PostHistory);

        var history = board.Last(EventBoardSubOpcode.PostHistory);
        history.ReadUInt().Should().Be(1u);
        history.ReadShort().Should().Be(2);
        history.ReadByte().Should().Be(EventPostRules.ContinuePlaying);
        history.ReadUInt().Should().Be((uint)Noon(11).ToUnixTimeSeconds());
        history.ReadByte().Should().Be(EventPostRules.LoveKnightOnline);
        history.ReadUInt().Should().Be((uint)Noon(10).ToUnixTimeSeconds());
    }

    [Fact]
    public async Task AnUnknownEventIsNotShown()
    {
        using var board = Open(Noon(10));

        await board.SendAsync(EventBoardSubOpcode.PostBoard, 7u);

        var reply = board.Last(EventBoardSubOpcode.PostBoard);
        reply.ReadUInt().Should().Be(7u);
        reply.ReadUInt().Should().Be(EventPostPacketWriter.Failed);
        reply.RemainingBytes.Should().Be(0);
    }
}
