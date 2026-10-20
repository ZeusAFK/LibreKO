using System.Collections.Concurrent;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface ISocialPacketCoordinator
{
    Task HandleChatTargetAsync(IClient client, Packet packet);
    Task HandleFriendProcessAsync(IClient client, Packet packet);
    Task NotifyFriendLoggedInAsync(UserSession session);
}

public class SocialPacketCoordinator(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    ILogger<SocialPacketCoordinator> logger) : ISocialPacketCoordinator
{
    private const int MaxFriendNameLength = 20;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private const byte RequestAccepted = 1;

    private readonly ConcurrentDictionary<(int Target, int Requester), DateTime> pendingRequests = new();

    public async Task HandleChatTargetAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        switch (packet.ReadByte())
        {
            case ChatTargetPacketWriter.TypeWhisper:
                await SelectWhisperTargetAsync(client, session, packet);
                break;

            case ChatTargetPacketWriter.TypeBlockToggle:
                session.BlockPrivateChat = packet.ReadByte() != 0;
                break;

            case ChatTargetPacketWriter.TypeClanAdmission when packet.RemainingBytes >= 1:
                await RequestClanConsultationAsync(session, packet);
                break;
        }
    }

    private const byte ConsultClan = 0;

    private async Task RequestClanConsultationAsync(UserSession session, Packet packet)
    {
        UserSession? target = null;
        if (packet.ReadByte() == ConsultClan)
        {
            if (packet.RemainingBytes >= 2 && sessionManager.Knights.GetClan(packet.ReadShort()) is { } clan)
                target = sessionManager.GetByName(clan.Chief)
                    ?? sessionManager.GetAll().FirstOrDefault(s => s.KnightsId == clan.Id && ClanRules.CanInvite(s.KnightsFame));
        }
        else if (packet.RemainingBytes >= 2)
        {
            target = sessionManager.GetByName(packet.ReadString());
        }

        if (target == null || target == session || target.BlockPrivateChat || session.BlockPrivateChat)
        {
            await session.Client.SendPacket(ChatTargetPacketWriter.ClanAdmissionFailed());
            return;
        }

        await session.Client.SendPacket(ChatTargetPacketWriter.ClanAdmissionRequestSent(target.Name));
        await target.Client.SendPacket(ChatTargetPacketWriter.ClanAdmissionRequestReceived(session.Name));
    }

    private async Task SelectWhisperTargetAsync(IClient client, UserSession session, Packet packet)
    {
        var targetName = packet.ReadString();
        if (string.IsNullOrEmpty(targetName) || targetName.Length > ChatTargetPacketWriter.MaxNameLength)
            return;

        var target = sessionManager.GetByName(targetName);

        Packet writer;
        if (session.BlockPrivateChat)
        {
            writer = ChatTargetPacketWriter.WhisperSenderBlocked(targetName);
        }
        else if (target == null || target == session)
        {
            writer = ChatTargetPacketWriter.WhisperTargetNotFound();
        }
        else if (target.BlockPrivateChat)
        {
            writer = ChatTargetPacketWriter.WhisperTargetBlocked(target.Name);
        }
        else
        {
            session.PrivateChatUser = target.CharacterId;
            writer = ChatTargetPacketWriter.WhisperConnected(target.Name);
        }

        await client.SendPacket(writer);
    }

    public async Task HandleFriendProcessAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null)
            return;

        var opcode = packet.ReadByte();
        switch (opcode)
        {
            case 1:
                await HandleFriendRequestAsync(client, session);
                break;

            case (byte)FriendSubOpcode.Add:
                await HandleFriendAddAsync(client, session, packet);
                break;

            case (byte)FriendSubOpcode.Remove:
                await HandleFriendRemoveAsync(client, session, packet);
                break;

            case (byte)FriendSubOpcode.Request:
                await HandleFriendAnswerAsync(session, packet);
                break;
        }
    }

    public async Task NotifyFriendLoggedInAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var watchers = await db.Friendships
            .Where(f => f.FriendCharacterId == session.CharacterId)
            .Select(f => f.CharacterId)
            .ToListAsync();
        foreach (var watcherId in watchers)
            if (sessionManager.GetByCharacterId(watcherId) is { } watcher)
                await watcher.Client.SendPacket(FriendPacketWriter.LoggedIn(session.Name));
    }

    private async Task HandleFriendRequestAsync(IClient client, UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var names = await db.Friendships
            .Where(f => f.CharacterId == session.CharacterId)
            .OrderBy(f => f.AddedAt)
            .Join(db.Characters, f => f.FriendCharacterId, c => c.Id, (_, c) => c.Name)
            .ToListAsync();
        var friends = names.Select(FriendStatus).ToList();
        await client.SendPacket(FriendPacketWriter.StatusList(friends, (ushort)friends.Count));
        await SendFriendDetailsAsync(client, db, names);
    }

    private async Task SendFriendDetailsAsync(IClient client, AppDbContext db, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return;

        var rows = await db.Characters
            .Where(character => names.Contains(character.Name))
            .Join(db.Accounts,
                character => character.AccountId,
                account => account.Id,
                (character, account) => new
                {
                    character.Name,
                    character.Level,
                    character.Class,
                    account.Nation,
                })
            .ToListAsync();

        var details = new List<FriendPacketWriter.Detail>(rows.Count);
        foreach (var row in rows)
        {
            var online = sessionManager.GetByName(row.Name);
            details.Add(new FriendPacketWriter.Detail(
                row.Name, row.Level, row.Class, (byte)row.Nation,
                online?.ZoneId ?? FriendPacketWriter.NoZone));
        }

        await client.SendPacket(FriendPacketWriter.Details(details));
    }

    private async Task HandleFriendAddAsync(IClient client, UserSession session, Packet packet)
    {
        var targetName = packet.ReadString();
        var target = string.IsNullOrEmpty(targetName) || targetName.Length > MaxFriendNameLength
            || targetName.Equals(session.Name, StringComparison.OrdinalIgnoreCase)
                ? null
                : sessionManager.GetByName(targetName);
        if (target == null)
        {
            await client.SendPacket(FriendPacketWriter.RequestResult(FriendRequestResult.CannotAdd));
            return;
        }

        if (target.Nation != session.Nation)
        {
            await client.SendPacket(FriendPacketWriter.RequestResult(FriendRequestResult.NationMismatch));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await FriendAddRefusalAsync(db, session.CharacterId, target.CharacterId) is { } refusal)
        {
            await client.SendPacket(FriendPacketWriter.RequestResult(refusal));
            return;
        }

        var key = (target.CharacterId, session.CharacterId);
        if (pendingRequests.TryGetValue(key, out var askedAt) && DateTime.UtcNow - askedAt < RequestTimeout)
        {
            await client.SendPacket(FriendPacketWriter.RequestResult(FriendRequestResult.InProgress));
            return;
        }

        pendingRequests[key] = DateTime.UtcNow;
        await target.Client.SendPacket(FriendPacketWriter.Request(session.Name));
        logger.LogDebug("{Name} asked {FriendName} to be friends", session.Name, target.Name);
    }

    private async Task HandleFriendAnswerAsync(UserSession session, Packet packet)
    {
        var requesterName = packet.ReadString();
        var accepted = packet.RemainingBytes >= 1 && packet.ReadByte() == RequestAccepted;
        var requester = string.IsNullOrEmpty(requesterName) ? null : sessionManager.GetByName(requesterName);
        if (requester == null || !pendingRequests.TryRemove((session.CharacterId, requester.CharacterId), out var askedAt))
            return;

        if (DateTime.UtcNow - askedAt >= RequestTimeout)
        {
            await requester.Client.SendPacket(FriendPacketWriter.RequestResult(FriendRequestResult.Expired));
            return;
        }

        if (!accepted)
        {
            await requester.Client.SendPacket(FriendPacketWriter.RequestResult(FriendRequestResult.Denied));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await FriendAddRefusalAsync(db, requester.CharacterId, session.CharacterId) is { } refusal)
        {
            await requester.Client.SendPacket(refusal == FriendRequestResult.ListFull
                ? FriendPacketWriter.AddResult(FriendAddResult.ListFull)
                : FriendPacketWriter.AddResult(FriendAddResult.Failed));
            return;
        }

        db.Friendships.Add(new Friendship
        {
            CharacterId = requester.CharacterId,
            FriendCharacterId = session.CharacterId,
            AddedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        logger.LogDebug("{Name} added friend {FriendName}", requester.Name, session.Name);
        await requester.Client.SendPacket(FriendPacketWriter.AddResult(FriendAddResult.Succeeded, FriendStatus(session.Name)));
    }

    private static async Task<FriendRequestResult?> FriendAddRefusalAsync(AppDbContext db, int characterId, int friendId)
    {
        if (await db.Friendships.AnyAsync(f => f.CharacterId == characterId && f.FriendCharacterId == friendId))
            return FriendRequestResult.AlreadyRegistered;
        if (await db.Friendships.CountAsync(f => f.CharacterId == characterId) >= Friendship.MaxFriends)
            return FriendRequestResult.ListFull;
        return null;
    }

    private async Task HandleFriendRemoveAsync(IClient client, UserSession session, Packet packet)
    {
        var targetName = packet.ReadString();

        if (string.IsNullOrEmpty(targetName) || targetName.Length > MaxFriendNameLength)
        {
            await client.SendPacket(FriendPacketWriter.RemoveResult(FriendRemoveResult.Failed, targetName ?? string.Empty));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var friendship = await db.Friendships
            .Where(f => f.CharacterId == session.CharacterId)
            .Join(db.Characters, f => f.FriendCharacterId, c => c.Id, (f, c) => new { f, c.Name })
            .Where(x => x.Name == targetName)
            .Select(x => x.f)
            .FirstOrDefaultAsync();
        if (friendship == null)
        {
            await client.SendPacket(FriendPacketWriter.RemoveResult(FriendRemoveResult.NotOnTheList, targetName));
            return;
        }

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync();
        logger.LogDebug("{Name} removed friend {FriendName}", session.Name, targetName);

        await client.SendPacket(FriendPacketWriter.RemoveResult(FriendRemoveResult.Succeeded, targetName));
    }

    private FriendPacketWriter.Status FriendStatus(string friendName)
    {
        var friend = sessionManager.GetByName(friendName);
        if (friend == null)
        {
            return new FriendPacketWriter.Status(
                friendName, FriendPacketWriter.OfflineCharacterId, FriendState.Offline);
        }

        return new FriendPacketWriter.Status(
            friendName,
            friend.CharacterId,
            friend.IsInParty ? FriendState.InParty : FriendState.Online);
    }
}
