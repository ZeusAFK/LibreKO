using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.Protocol;

public readonly record struct ChatRoomSeat(string Key, bool Public, ushort RoomId, int[] Members, int OwnerId = 0);

public interface INationChatService
{
    Task HandleAsync(IClient client, Packet packet);
    Task SayAsync(UserSession session, ushort roomId, string text);
    Task LeaveAllAsync(UserSession session);
    bool TryGetSeat(int characterId, out ChatRoomSeat seat);
    int[] MembersOf(string roomKey);
    Task SetKeptAsync(UserSession session, bool keep);
    Task RestoreKeptAsync();
    Task<int> PruneKeptAsync(DateTime unusedBefore);
    event Action<int, string>? SeatReleased;
}

public sealed class NationChatService(
    SessionManager sessionManager,
    IChatRoomHistory history,
    ILogger<NationChatService> logger,
    IOptions<GameServerSettings>? settings = null,
    TimeProvider? clock = null) : INationChatService
{
    public const int PublicCapacity = 500;
    public const int TitleMaxLength = 30;
    public const int PasswordMaxLength = 8;
    public const int PrivateListMax = 500;
    public const ushort PrivateRoomIdBase = 1000;
    public static readonly ushort[] RoomSizes = [10, 30, 50, 100, 150, 200];
    public static readonly string[] PublicChannelNames = ["General", "Trade", "Party", "Help"];

    private sealed class PublicChannel(ushort id, string name)
    {
        public ushort Id { get; } = id;
        public string Name { get; } = name;
        public HashSet<int> Members { get; } = [];
        public string Key => $"pub:{Id}";
    }

    private sealed class PrivateRoom
    {
        public ushort Id { get; init; }
        public string Key { get; init; } = $"room:{Guid.NewGuid():N}";
        public string Name { get; set; } = "";
        public string Password { get; set; } = "";
        public ushort MaxMembers { get; set; }
        public int OwnerId { get; set; }
        public List<int> Members { get; } = [];
        public bool Kept { get; set; }
        public DateTime CreatedAt { get; init; }
        public DateTime LastUsedAt { get; set; }

        public bool Locked => Password.Length > 0;
    }

    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    private int KeptRoomsPerCharacter => settings?.Value.ChatRooms.KeptRoomsPerCharacter ?? new ChatRoomSettings().KeptRoomsPerCharacter;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private readonly record struct Seat(bool Public, ushort RoomId);

    private readonly object gate = new();
    private readonly List<PublicChannel> channels = PublicChannelNames
        .Select((name, i) => new PublicChannel((ushort)(i + 1), name)).ToList();
    private readonly Dictionary<ushort, PrivateRoom> rooms = [];
    private readonly Dictionary<int, Seat> seats = [];
    private ushort nextRoomId = PrivateRoomIdBase;

    public event Action<int, string>? SeatReleased;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 2)
            return;
        if (packet.ReadByte() != NationChatPacketWriter.NationRooms)
            return;

        byte sub = packet.ReadByte();
        switch (sub)
        {
            case NationChatPacketWriter.PublicList: await SendChannelsAsync(session); break;
            case NationChatPacketWriter.PublicEnter: await EnterPublicAsync(session, packet); break;
            case NationChatPacketWriter.PublicLeave: await LeavePublicAsync(session); break;
            case NationChatPacketWriter.PrivateList: await SendRoomsAsync(session); break;
            case NationChatPacketWriter.Create: await CreateAsync(session, packet); break;
            case NationChatPacketWriter.PrivateEnter: await EnterPrivateAsync(session, packet); break;
            case NationChatPacketWriter.PrivateLeave: await LeavePrivateAsync(session); break;
            case NationChatPacketWriter.Search: await SearchAsync(session, packet); break;
            case NationChatPacketWriter.Options: await ChangeOptionsAsync(session, packet); break;
            case NationChatPacketWriter.HandOver: await HandOverAsync(session, packet); break;
            case NationChatPacketWriter.Kick: await KickAsync(session, packet); break;
            case NationChatPacketWriter.Members: await SendMembersAsync(session, packet); break;
            default: logger.LogDebug("Unhandled nation chat sub {Sub} from {Name}", sub, session.Name); break;
        }
    }

    public async Task SayAsync(UserSession session, ushort roomId, string text)
    {
        if (!TryGetSeat(session.CharacterId, out var seat) || seat.RoomId != roomId)
            return;
        await SendToAsync(seat.Members, NationChatPacketWriter.Say(session.CharacterId, session.Name, text, session.ZoneId));
        if (session.IsBot) return;
        ChatRoomMessage? line = null;
        try { line = await history.AppendTextAsync(seat.Key, session.CharacterId, session.Name, text); }
        catch (Exception ex) { logger.LogWarning(ex, "Room line from {Name} was not stored", session.Name); }
        if (line != null)
            await SendToAsync(MembersOf(seat.Key), RoomMediaPacketWriter.StoredRow(line));
    }

    public async Task SetKeptAsync(UserSession session, bool keep)
    {
        byte result;
        List<int> members = [];
        KeptChatRoom? save = null;
        string? drop = null;
        lock (gate)
        {
            if (!seats.TryGetValue(session.CharacterId, out var seat) || seat.Public || !rooms.TryGetValue(seat.RoomId, out var room))
                result = RoomMediaResults.NotInRoom;
            else if (room.OwnerId != session.CharacterId)
                result = RoomMediaResults.NotLeader;
            else if (keep && !room.Kept && rooms.Values.Count(r => r.Kept && r.OwnerId == session.CharacterId) >= KeptRoomsPerCharacter)
                result = RoomMediaResults.KeptLimit;
            else
            {
                result = RoomMediaResults.Ok;
                if (room.Kept != keep)
                {
                    room.Kept = keep;
                    room.LastUsedAt = Now;
                    members = [.. room.Members];
                    if (keep) save = KeptRowOf(room);
                    else drop = room.Key;
                }
            }
        }
        if (result != RoomMediaResults.Ok)
        {
            await session.Client.SendPacket(RoomMediaPacketWriter.KeptState(result, false));
            return;
        }
        if (save != null) await SaveKeptAsync(save);
        if (drop != null) await DropKeptAsync(drop);
        if (members.Count > 0)
            await SendToAsync(members, RoomMediaPacketWriter.KeptState(RoomMediaResults.Ok, keep));
    }

    public async Task RestoreKeptAsync()
    {
        IReadOnlyList<KeptChatRoom> rows;
        try { rows = await history.KeptRoomsAsync(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kept chat rooms could not be loaded");
            return;
        }
        int restored = 0;
        lock (gate)
        {
            foreach (var row in rows)
            {
                if (nextRoomId == ushort.MaxValue || rooms.Values.Any(r => r.Key == row.RoomKey)) continue;
                var room = new PrivateRoom
                {
                    Id = nextRoomId++,
                    Key = row.RoomKey,
                    Name = row.Name,
                    Password = row.Password,
                    MaxMembers = RoomSize((ushort)Math.Clamp(row.MaxMembers, 0, ushort.MaxValue)),
                    OwnerId = row.OwnerId,
                    Kept = true,
                    CreatedAt = row.CreatedAt,
                    LastUsedAt = row.LastUsedAt,
                };
                rooms[room.Id] = room;
                restored++;
            }
        }
        if (restored > 0) logger.LogInformation("Restored {Count} kept chat rooms", restored);
    }

    public async Task<int> PruneKeptAsync(DateTime unusedBefore)
    {
        List<string> keys;
        lock (gate)
        {
            var stale = rooms.Values.Where(r => r.Kept && r.Members.Count == 0 && r.LastUsedAt < unusedBefore).ToList();
            foreach (var room in stale) rooms.Remove(room.Id);
            keys = stale.Select(r => r.Key).ToList();
        }
        foreach (string key in keys) await DropKeptAsync(key);
        return keys.Count;
    }

    private async Task SaveKeptAsync(KeptChatRoom row)
    {
        try { await history.SaveKeptRoomAsync(row); }
        catch (Exception ex) { logger.LogWarning(ex, "Kept chat room {Key} was not saved", row.RoomKey); }
    }

    private async Task DropKeptAsync(string key)
    {
        try { await history.DeleteKeptRoomAsync(key); }
        catch (Exception ex) { logger.LogWarning(ex, "Kept chat room {Key} was not deleted", key); }
    }

    private static KeptChatRoom KeptRowOf(PrivateRoom room) => new()
    {
        RoomKey = room.Key,
        Name = room.Name,
        Password = room.Password,
        MaxMembers = room.MaxMembers,
        OwnerId = room.OwnerId,
        CreatedAt = room.CreatedAt,
        LastUsedAt = room.LastUsedAt,
    };

    public bool TryGetSeat(int characterId, out ChatRoomSeat seat)
    {
        lock (gate)
        {
            seat = default;
            if (!seats.TryGetValue(characterId, out var found))
                return false;
            if (found.Public)
            {
                var channel = channels.First(c => c.Id == found.RoomId);
                seat = new ChatRoomSeat(channel.Key, true, found.RoomId, [.. channel.Members]);
                return true;
            }
            if (!rooms.TryGetValue(found.RoomId, out var room))
                return false;
            seat = new ChatRoomSeat(room.Key, false, room.Id, [.. room.Members], room.OwnerId);
            return true;
        }
    }

    public int[] MembersOf(string roomKey)
    {
        lock (gate)
        {
            if (rooms.Values.FirstOrDefault(r => r.Key == roomKey) is { } room)
                return [.. room.Members];
            return channels.FirstOrDefault(c => c.Key == roomKey) is { } channel ? [.. channel.Members] : [];
        }
    }

    private void Released(int characterId, string key)
    {
        try { SeatReleased?.Invoke(characterId, key); }
        catch (Exception ex) { logger.LogWarning(ex, "Room seat release handler failed for {Id}", characterId); }
    }

    public async Task LeaveAllAsync(UserSession session)
    {
        Seat seat;
        lock (gate)
        {
            if (!seats.TryGetValue(session.CharacterId, out seat))
                return;
        }
        if (seat.Public) await LeavePublicAsync(session, reply: false);
        else await LeavePrivateAsync(session, reply: false);
    }

    private Task SendChannelsAsync(UserSession session)
    {
        List<NationChatPacketWriter.ChannelRow> rows;
        lock (gate)
            rows = channels.Select(c => new NationChatPacketWriter.ChannelRow(c.Id, c.Name, (ushort)c.Members.Count)).ToList();
        return session.Client.SendPacket(NationChatPacketWriter.ChannelList(rows));
    }

    private async Task EnterPublicAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2) return;
        ushort id = packet.ReadUShort();
        byte result;
        lock (gate)
        {
            var channel = channels.FirstOrDefault(c => c.Id == id);
            if (seats.ContainsKey(session.CharacterId)) result = NationChatResults.AlreadyInRoom;
            else if (channel == null) result = NationChatResults.NoRoom;
            else if (channel.Members.Count >= PublicCapacity) result = NationChatResults.PublicFull;
            else
            {
                channel.Members.Add(session.CharacterId);
                seats[session.CharacterId] = new Seat(true, id);
                result = NationChatResults.Ok;
            }
        }
        await session.Client.SendPacket(NationChatPacketWriter.Result(NationChatPacketWriter.PublicEnter, result));
    }

    private Task LeavePublicAsync(UserSession session) => LeavePublicAsync(session, reply: true);

    private async Task LeavePublicAsync(UserSession session, bool reply)
    {
        string key;
        lock (gate)
        {
            if (!seats.TryGetValue(session.CharacterId, out var seat) || !seat.Public)
                return;
            var channel = channels.First(c => c.Id == seat.RoomId);
            channel.Members.Remove(session.CharacterId);
            seats.Remove(session.CharacterId);
            key = channel.Key;
        }
        Released(session.CharacterId, key);
        if (reply)
            await session.Client.SendPacket(NationChatPacketWriter.Result(NationChatPacketWriter.PublicLeave, NationChatResults.Ok));
    }

    private Task SendRoomsAsync(UserSession session)
    {
        List<NationChatPacketWriter.RoomRow> rows;
        lock (gate)
            rows = rooms.Values.OrderBy(r => r.Id).Take(PrivateListMax).Select(r => RowOf(r, session.Nation)).ToList();
        return session.Client.SendPacket(NationChatPacketWriter.RoomList(rows));
    }

    private async Task CreateAsync(UserSession session, Packet packet)
    {
        if (!TryReadRoomSettings(packet, out var name, out bool isPrivate, out var password, out ushort maxMembers))
            return;

        byte result;
        NationChatPacketWriter.RoomInfo info = default;
        lock (gate)
        {
            result = SettingsError(name, isPrivate, password, NationChatResults.CreateNameLength, NationChatResults.CreatePasswordLength);
            if (result == NationChatResults.Ok && (seats.ContainsKey(session.CharacterId) || nextRoomId == ushort.MaxValue))
                result = NationChatResults.CreateRefused;
            if (result == NationChatResults.Ok)
            {
                var room = new PrivateRoom
                {
                    Id = nextRoomId++,
                    Name = name,
                    Password = isPrivate ? password : "",
                    MaxMembers = RoomSize(maxMembers),
                    OwnerId = session.CharacterId,
                    CreatedAt = Now,
                    LastUsedAt = Now,
                };
                room.Members.Add(session.CharacterId);
                rooms[room.Id] = room;
                seats[session.CharacterId] = new Seat(false, room.Id);
                info = InfoOf(room);
                result = NationChatResults.Created;
            }
        }
        await session.Client.SendPacket(result == NationChatResults.Created
            ? NationChatPacketWriter.RoomCreated(info)
            : NationChatPacketWriter.Result(NationChatPacketWriter.Create, result));
    }

    private async Task EnterPrivateAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 3) return;
        ushort roomId = packet.ReadUShort();
        bool withPassword = packet.ReadByte() != 0;
        string password = withPassword && packet.RemainingBytes >= 2 ? packet.ReadString() : "";

        byte result;
        List<int> others = [];
        ushort count = 0;
        bool kept = false;
        NationChatPacketWriter.RoomInfo? leading = null;
        lock (gate)
        {
            rooms.TryGetValue(roomId, out var room);
            if (seats.ContainsKey(session.CharacterId)) result = NationChatResults.AlreadyInRoom;
            else if (room == null) result = NationChatResults.NoRoom;
            else if (room.Locked && password.Length is 0 or > PasswordMaxLength) result = NationChatResults.PasswordLength;
            else if (room.Locked && password != room.Password) result = NationChatResults.PasswordWrong;
            else if (room.Members.Count >= room.MaxMembers) result = NationChatResults.RoomFull;
            else
            {
                others = [.. room.Members];
                room.Members.Add(session.CharacterId);
                room.LastUsedAt = Now;
                seats[session.CharacterId] = new Seat(false, roomId);
                count = (ushort)room.Members.Count;
                kept = room.Kept;
                if (room.OwnerId == session.CharacterId) leading = InfoOf(room);
                result = NationChatResults.Ok;
            }
        }
        await session.Client.SendPacket(result == NationChatResults.Ok
            ? NationChatPacketWriter.Entered(roomId)
            : NationChatPacketWriter.Result(NationChatPacketWriter.PrivateEnter, result));
        if (result != NationChatResults.Ok) return;
        if (leading is { } info) await session.Client.SendPacket(NationChatPacketWriter.NewOwner(info));
        await session.Client.SendPacket(RoomMediaPacketWriter.KeptState(RoomMediaResults.Ok, kept));
        await SendToAsync(others, NationChatPacketWriter.MemberMoved(NationChatPacketWriter.SayEntered, count, session.Name));
    }

    private Task LeavePrivateAsync(UserSession session) => LeavePrivateAsync(session, reply: true);

    private async Task LeavePrivateAsync(UserSession session, bool reply)
    {
        List<int> rest;
        ushort count;
        UserSession? heir = null;
        PrivateRoom room;
        KeptChatRoom? save = null;
        lock (gate)
        {
            if (!seats.TryGetValue(session.CharacterId, out var seat) || seat.Public || !rooms.TryGetValue(seat.RoomId, out room!))
                return;
            room.Members.Remove(session.CharacterId);
            seats.Remove(session.CharacterId);
            room.LastUsedAt = Now;
            if (room.Kept)
            {
                if (room.Members.Count == 0) save = KeptRowOf(room);
            }
            else if (room.Members.Count == 0)
                rooms.Remove(room.Id);
            else if (room.OwnerId == session.CharacterId)
            {
                room.OwnerId = room.Members[0];
                heir = sessionManager.GetByCharacterId(room.OwnerId);
            }
            rest = [.. room.Members];
            count = (ushort)rest.Count;
        }
        Released(session.CharacterId, room.Key);
        if (save != null) await SaveKeptAsync(save);
        if (reply)
            await session.Client.SendPacket(NationChatPacketWriter.Result(NationChatPacketWriter.PrivateLeave, NationChatResults.Ok));
        if (rest.Count == 0) return;
        await SendToAsync(rest, NationChatPacketWriter.MemberMoved(NationChatPacketWriter.SayLeft, count, session.Name));
        if (heir != null)
        {
            await heir.Client.SendPacket(NationChatPacketWriter.NewOwner(InfoOf(room)));
            await SendToAsync(rest, NationChatPacketWriter.NamedEvent(NationChatPacketWriter.SayNewOwner, heir.Name));
        }
    }

    private async Task SearchAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2) return;
        string word = packet.ReadString().Trim();
        if (word.Length is 0 or > TitleMaxLength)
        {
            await session.Client.SendPacket(NationChatPacketWriter.SearchResult(NationChatResults.SearchNameLength, []));
            return;
        }
        List<NationChatPacketWriter.RoomRow> rows;
        lock (gate)
            rows = rooms.Values.Where(r => r.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Id).Take(PrivateListMax).Select(r => RowOf(r, session.Nation)).ToList();
        await session.Client.SendPacket(NationChatPacketWriter.SearchResult(
            rows.Count == 0 ? NationChatResults.SearchNone : NationChatResults.Ok, rows));
    }

    private async Task ChangeOptionsAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2) return;
        ushort roomId = packet.ReadUShort();
        if (!TryReadRoomSettings(packet, out var name, out bool isPrivate, out var password, out ushort maxMembers))
            return;

        byte result;
        KeptChatRoom? save = null;
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var room)) result = NationChatResults.OptionsNoRoom;
            else if (room.OwnerId != session.CharacterId) result = NationChatResults.NotOwner;
            else
            {
                result = SettingsError(name, isPrivate, password, NationChatResults.OptionsNameLength, NationChatResults.OptionsPasswordLength);
                if (result == NationChatResults.Ok)
                {
                    room.Name = name;
                    room.Password = isPrivate ? password : "";
                    room.MaxMembers = (ushort)Math.Max(RoomSize(maxMembers), room.Members.Count);
                    if (room.Kept) save = KeptRowOf(room);
                }
            }
        }
        if (save != null) await SaveKeptAsync(save);
        await session.Client.SendPacket(NationChatPacketWriter.Result(NationChatPacketWriter.Options, result));
    }

    private async Task HandOverAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 6) return;
        ushort roomId = packet.ReadUShort();
        int targetId = packet.ReadInt();

        byte result;
        UserSession? heir = null;
        List<int> members = [];
        NationChatPacketWriter.RoomInfo info = default;
        KeptChatRoom? save = null;
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var room)) result = NationChatResults.OptionsNoRoom;
            else if (room.OwnerId != session.CharacterId) result = NationChatResults.NotOwner;
            else if (targetId == session.CharacterId || !room.Members.Contains(targetId)
                || (heir = sessionManager.GetByCharacterId(targetId)) == null)
                result = NationChatResults.HandOverNoTarget;
            else
            {
                room.OwnerId = targetId;
                members = [.. room.Members];
                info = InfoOf(room);
                if (room.Kept) save = KeptRowOf(room);
                result = NationChatResults.Ok;
            }
        }
        if (save != null) await SaveKeptAsync(save);
        await session.Client.SendPacket(NationChatPacketWriter.Result(NationChatPacketWriter.HandOver, result));
        if (result != NationChatResults.Ok || heir == null) return;
        await heir.Client.SendPacket(NationChatPacketWriter.NewOwner(info));
        await SendToAsync(members, NationChatPacketWriter.NamedEvent(NationChatPacketWriter.SayNewOwner, heir.Name));
    }

    private async Task KickAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 6) return;
        ushort roomId = packet.ReadUShort();
        int targetId = packet.ReadInt();

        byte result;
        UserSession? target = null;
        List<int> rest = [];
        string key = "";
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var room)) result = NationChatResults.OptionsNoRoom;
            else if (room.OwnerId != session.CharacterId) result = NationChatResults.NotOwner;
            else if (targetId == session.CharacterId) result = NationChatResults.KickSelf;
            else if (!room.Members.Remove(targetId)) result = NationChatResults.KickNoTarget;
            else
            {
                seats.Remove(targetId);
                target = sessionManager.GetByCharacterId(targetId);
                rest = [.. room.Members];
                key = room.Key;
                result = NationChatResults.Ok;
            }
        }
        await session.Client.SendPacket(NationChatPacketWriter.Result(NationChatPacketWriter.Kick, result));
        if (result != NationChatResults.Ok) return;
        Released(targetId, key);
        if (target != null)
            await target.Client.SendPacket(NationChatPacketWriter.Result(NationChatPacketWriter.Kick, NationChatResults.YouWereKicked));
        string name = target?.Name ?? "";
        await SendToAsync(rest, NationChatPacketWriter.NamedEvent(NationChatPacketWriter.SayKicked, name));
    }

    private async Task SendMembersAsync(UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 2) return;
        ushort roomId = packet.ReadUShort();
        Packet reply;
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var room) || !room.Members.Contains(session.CharacterId))
                reply = NationChatPacketWriter.Result(NationChatPacketWriter.Members, NationChatResults.MembersNoRoom);
            else
            {
                var rows = room.Members
                    .Select(id => new NationChatPacketWriter.MemberRow(id, sessionManager.GetByCharacterId(id)?.Name ?? ""))
                    .ToList();
                reply = NationChatPacketWriter.MemberList(room.Id, room.Name, room.OwnerId, room.MaxMembers, rows);
            }
        }
        await session.Client.SendPacket(reply);
    }

    private static bool TryReadRoomSettings(Packet packet, out string name, out bool isPrivate, out string password, out ushort maxMembers)
    {
        name = "";
        password = "";
        isPrivate = false;
        maxMembers = 0;
        if (packet.RemainingBytes < 2) return false;
        name = packet.ReadString().Trim();
        if (packet.RemainingBytes < 1) return false;
        isPrivate = packet.ReadByte() != 0;
        if (isPrivate)
        {
            if (packet.RemainingBytes < 2) return false;
            password = packet.ReadString();
        }
        if (packet.RemainingBytes < 2) return false;
        maxMembers = packet.ReadUShort();
        return true;
    }

    private static byte SettingsError(string name, bool isPrivate, string password, byte nameError, byte passwordError) =>
        name.Length is 0 or > TitleMaxLength ? nameError
        : isPrivate && password.Length is 0 or > PasswordMaxLength ? passwordError
        : NationChatResults.Ok;

    private static ushort RoomSize(ushort asked) =>
        RoomSizes.Contains(asked) ? asked : RoomSizes[0];

    private static NationChatPacketWriter.RoomRow RowOf(PrivateRoom room, AccountNation viewer) =>
        new(room.Id, room.Name, room.Locked, (byte)viewer, (ushort)room.Members.Count, room.MaxMembers);

    private static NationChatPacketWriter.RoomInfo InfoOf(PrivateRoom room) =>
        new(room.Id, room.Name, room.Locked, room.Password, room.MaxMembers);

    private async Task SendToAsync(IEnumerable<int> characterIds, Packet packet)
    {
        foreach (var id in characterIds)
        {
            var member = sessionManager.GetByCharacterId(id);
            if (member == null) continue;
            try { await member.Client.SendPacket(packet); }
            catch (Exception ex) { logger.LogDebug(ex, "Nation chat send failed for {Id}", id); }
        }
    }
}
