using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Protocol;

public interface IEventPostPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class EventPostPacketCoordinator(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    IServerRepository serverRepository,
    IItemGrantService itemGrant,
    IGameDataService gameData,
    IOptions<GameServerSettings> settings,
    ILogger<EventPostPacketCoordinator> logger,
    TimeProvider? clock = null) : IEventPostPacketCoordinator
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private const string BagFull = "Make room in your bag for the reward first.";

    private readonly ConcurrentDictionary<int, byte> posting = new();
    private string? serverName;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        switch ((EventBoardSubOpcode)packet.ReadByte())
        {
            case EventBoardSubOpcode.PostBoard when packet.RemainingBytes >= sizeof(uint):
                await ShowBoardAsync(session, packet.ReadUInt());
                return;
            case EventBoardSubOpcode.PostRegister when packet.RemainingBytes > sizeof(uint):
                var eventId = packet.ReadUInt();
                await RegisterAsync(session, eventId, packet.ReadSByteString());
                return;
            case EventBoardSubOpcode.PostHistory:
                await ShowHistoryAsync(session);
                return;
        }
    }

    private EventPostCalendar Calendar() =>
        EventPostRules.Calendar(time.GetUtcNow().UtcDateTime, ScheduleTime.Zone(settings.Value.Events));

    private async Task ShowBoardAsync(UserSession session, uint eventId)
    {
        var calendar = Calendar();
        if (!EventPostRules.IsEvent(eventId))
        {
            await session.Client.SendPacket(EventPostPacketWriter.Board(eventId, null, calendar));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var board = await BoardAsync(scope, (byte)eventId, calendar);
        await session.Client.SendPacket(EventPostPacketWriter.Board(eventId, board, calendar));
    }

    private async Task RegisterAsync(UserSession session, uint eventId, string text)
    {
        if (!EventPostRules.IsEvent(eventId))
        {
            await session.Client.SendPacket(EventPostPacketWriter.Registered(eventId, EventPostPacketWriter.Failed));
            return;
        }

        if (!EventPostRules.Matches((byte)eventId, text))
        {
            await session.Client.SendPacket(EventPostPacketWriter.Registered(eventId, EventPostPacketWriter.WrongText));
            return;
        }

        if (!posting.TryAdd(session.CharacterId, 0))
        {
            await session.Client.SendPacket(EventPostPacketWriter.Registered(eventId, EventPostPacketWriter.TryLater));
            return;
        }

        try
        {
            await session.Client.SendPacket(await PostAsync(session, (byte)eventId));
        }
        finally
        {
            posting.TryRemove(session.CharacterId, out _);
        }
    }

    private async Task<Packet> PostAsync(UserSession session, byte eventId)
    {
        var calendar = Calendar();
        using var scope = scopeFactory.CreateScope();
        var posts = scope.ServiceProvider.GetRequiredService<IEventPostRepository>();

        if (await posts.CountAsync(session.CharacterId, eventId, calendar.DayStart, calendar.DayEnd) > 0)
            return EventPostPacketWriter.Registered(eventId, EventPostPacketWriter.AlreadyPosted);

        var postsInPeriod = await posts.CountAsync(session.CharacterId, eventId, calendar.PeriodStart, calendar.PeriodEnd) + 1;
        var rewards = EventPostRules.Rewards(postsInPeriod).Select(gameData.GetItem).ToList();
        if (rewards.Any(item => item == null))
        {
            logger.LogWarning("Event post rewards for {Posts} posts name an item the tables lack", postsInPeriod);
            return EventPostPacketWriter.Registered(eventId, EventPostPacketWriter.Failed);
        }

        if (session.Trade.IsTrading || session.Trade.IsMerchanting)
            return EventPostPacketWriter.Registered(eventId, EventPostPacketWriter.Failed);

        if (session.FreeBagSlots < rewards.Count)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation, BagFull));
            return EventPostPacketWriter.Registered(eventId, EventPostPacketWriter.Failed);
        }

        await posts.AddAsync(new EventPost
        {
            EventId = eventId,
            CharacterId = session.CharacterId,
            CharacterName = session.Name,
            ServerName = await ServerNameAsync(),
            Text = EventPostRules.Phrase(eventId),
            PostedAt = time.GetUtcNow().UtcDateTime,
        });
        foreach (var item in rewards)
            await itemGrant.GrantAsync(session, item!, 1);

        logger.LogInformation("{Name} posted on event board {Event} ({Posts} this period), rewards {Rewards}",
            session.Name, eventId, postsInPeriod, string.Join(", ", rewards.Select(item => item!.Num)));
        var board = await BoardAsync(scope, eventId, calendar);
        return EventPostPacketWriter.Registered(eventId, EventPostPacketWriter.Accepted, board, calendar);
    }

    private async Task ShowHistoryAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var history = await scope.ServiceProvider.GetRequiredService<IEventPostRepository>()
            .HistoryAsync(session.CharacterId, EventPostRules.HistoryLimit);
        await session.Client.SendPacket(EventPostPacketWriter.History(history));
    }

    private static Task<EventPostBoard> BoardAsync(IServiceScope scope, byte eventId, EventPostCalendar calendar) =>
        scope.ServiceProvider.GetRequiredService<IEventPostRepository>().BoardAsync(
            eventId, calendar.DayStart, calendar.PeriodStart, calendar.PeriodEnd, EventPostRules.RecentLimit);

    private async Task<string> ServerNameAsync()
    {
        if (serverName != null)
            return serverName;
        var servers = await serverRepository.GetServers();
        var name = servers.FirstOrDefault(server => server.Id == settings.Value.ServerId)?.Name ?? string.Empty;
        return serverName = name.Length > EventPost.ServerNameMaxLength ? name[..EventPost.ServerNameMaxLength] : name;
    }
}
