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
}

public class SocialPacketCoordinator(
    SessionManager sessionManager,
    IServiceScopeFactory scopeFactory,
    ILogger<SocialPacketCoordinator> logger) : ISocialPacketCoordinator
{
    private const int MaxFriendNameLength = 20;

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
        }
    }

    private async Task SelectWhisperTargetAsync(IClient client, UserSession session, Packet packet)
    {
        var targetName = packet.ReadString();
        if (string.IsNullOrEmpty(targetName) || targetName.Length > ChatTargetPacketWriter.MaxNameLength)
            return;

        var target = sessionManager.GetByName(targetName);

        Packet writer;
        if (target == null || target == session)
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

            case 2:
                await HandleFriendReportAsync(client, packet);
                break;

            case 3:
                await HandleFriendAddAsync(client, session, packet);
                break;

            case 4:
                await HandleFriendRemoveAsync(client, session, packet);
                break;
        }
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

    private async Task HandleFriendReportAsync(IClient client, Packet packet)
    {
        var count = packet.ReadUShort();
        if (count > 24)
            return;

        var friends = new List<FriendPacketWriter.Status>(count);
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var friendName = packet.ReadSByteString();
            if (string.IsNullOrEmpty(friendName) || friendName.Length > MaxFriendNameLength)
                return;

            names.Add(friendName);
            friends.Add(FriendStatus(friendName));
        }

        await client.SendPacket(FriendPacketWriter.StatusList(friends, count));

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SendFriendDetailsAsync(client, db, names);
    }

    private async Task HandleFriendAddAsync(IClient client, UserSession session, Packet packet)
    {
        var targetName = packet.ReadSByteString();

        if (string.IsNullOrEmpty(targetName) || targetName.Length > 20 ||
            targetName.Equals(session.Name, StringComparison.OrdinalIgnoreCase))
        {
            await SendFriendModifyResultAsync(client, FriendSubOpcode.Add, (byte)FriendAddResult.Failed, targetName ?? string.Empty);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var target = await db.Characters
            .Where(c => c.Name == targetName)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync();
        if (target == null)
        {
            await SendFriendModifyResultAsync(client, FriendSubOpcode.Add, (byte)FriendAddResult.Failed, targetName);
            return;
        }

        var alreadyFriends = await db.Friendships
            .AnyAsync(f => f.CharacterId == session.CharacterId && f.FriendCharacterId == target.Id);
        if (alreadyFriends)
        {
            await SendFriendModifyResultAsync(client, FriendSubOpcode.Add, (byte)FriendAddResult.Failed, targetName);
            return;
        }

        var count = await db.Friendships.CountAsync(f => f.CharacterId == session.CharacterId);
        if (count >= Friendship.MaxFriends)
        {
            await SendFriendModifyResultAsync(client, FriendSubOpcode.Add, (byte)FriendAddResult.ListFull, targetName);
            return;
        }

        db.Friendships.Add(new Friendship
        {
            CharacterId = session.CharacterId,
            FriendCharacterId = target.Id,
            AddedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        logger.LogDebug("{Name} added friend {FriendName}", session.Name, targetName);

        await SendFriendModifyResultAsync(client, FriendSubOpcode.Add, (byte)FriendAddResult.Succeeded, targetName);
    }

    private async Task HandleFriendRemoveAsync(IClient client, UserSession session, Packet packet)
    {
        var targetName = packet.ReadSByteString();

        if (string.IsNullOrEmpty(targetName) || targetName.Length > 20)
        {
            await SendFriendModifyResultAsync(client, FriendSubOpcode.Remove, (byte)FriendRemoveResult.Failed, targetName ?? string.Empty);
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
            await SendFriendModifyResultAsync(client, FriendSubOpcode.Remove, (byte)FriendRemoveResult.NotOnTheList, targetName);
            return;
        }

        db.Friendships.Remove(friendship);
        await db.SaveChangesAsync();
        logger.LogDebug("{Name} removed friend {FriendName}", session.Name, targetName);

        await SendFriendModifyResultAsync(client, FriendSubOpcode.Remove, (byte)FriendRemoveResult.Succeeded, targetName);
    }

    private async Task SendFriendModifyResultAsync(IClient client, FriendSubOpcode sub, byte resultCode, string friendName)
    {
        await client.SendPacket(FriendPacketWriter.ModifyResult(
            sub, resultCode, friendName, FriendStatus(friendName)));
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
