using System.Collections.Generic;
using System.Linq;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IChatRoomPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class ChatRoomPacketCoordinator(
    SessionManager sessionManager,
    ILogger<ChatRoomPacketCoordinator> logger) : IChatRoomPacketCoordinator
{
    private const byte ChatRoomSubList = 1;
    private const byte ChatRoomSubCreate = 2;
    private const byte ChatRoomSubJoin = 3;
    private const byte ChatRoomSubLeave = 4;
    private const byte ChatRoomSubSay = 5;
    private const byte ChatRoomSubSayBroadcast = 6;

    private sealed class ChatRoom
    {
        public int RoomId;
        public string Name = string.Empty;
        public readonly HashSet<int> Members = new();
    }

    // All rooms, keyed by roomId. charId -> roomId tracks which room a character currently sits in.
    private static readonly Dictionary<int, ChatRoom> chatRooms = new();
    private static readonly Dictionary<int, int> chatRoomByChar = new();
    private static int chatRoomNextId = 1;
    private static readonly object chatRoomLock = new();

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case ChatRoomSubList:
                await SendListAsync(session);
                break;
            case ChatRoomSubCreate:
                await HandleCreateAsync(session, packet);
                break;
            case ChatRoomSubJoin:
                await HandleJoinAsync(session, packet);
                break;
            case ChatRoomSubLeave:
                await HandleLeaveAsync(session);
                break;
            case ChatRoomSubSay:
                await HandleSayAsync(session, packet);
                break;
        }
    }

    private async Task SendListAsync(UserSession session)
    {
        (int Id, string Name, int Count)[] snapshot;
        lock (chatRoomLock)
        {
            snapshot = chatRooms.Values
                .Select(r => (r.RoomId, r.Name, r.Members.Count))
                .ToArray();
        }

        var rooms = snapshot
            .Select(room => new ChatRoomPacketWriter.RoomEntry(room.Id, room.Name, (ushort)room.Count))
            .ToList();

        await session.Client.SendPacket(ChatRoomPacketWriter.RoomList(ChatRoomSubList, rooms));
    }

    private async Task HandleCreateAsync(UserSession session, Packet packet)
    {
        var name = packet.RemainingBytes >= 1 ? packet.ReadSByteString() : string.Empty;

        name = (name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 30)
        {
            await session.Client.SendPacket(ChatRoomPacketWriter.RoomResult(
                ChatRoomSubCreate, ChatRoomPacketWriter.Failed, ChatRoomPacketWriter.NoRoomId));
            return;
        }

        ChatRoom room;
        lock (chatRoomLock)
        {
            RemoveFromRoom(session.CharacterId);
            room = new ChatRoom { RoomId = chatRoomNextId++, Name = name };
            room.Members.Add(session.CharacterId);
            chatRooms[room.RoomId] = room;
            chatRoomByChar[session.CharacterId] = room.RoomId;
        }

        logger.LogDebug("{Name} created chat room {Room} ({Id})", session.Name, name, room.RoomId);
        await session.Client.SendPacket(ChatRoomPacketWriter.RoomResult(
            ChatRoomSubCreate, ChatRoomPacketWriter.Succeeded, room.RoomId));
    }

    private async Task HandleJoinAsync(UserSession session, Packet packet)
    {
        int roomId = packet.RemainingBytes >= 4 ? packet.ReadInt() : 0;

        bool ok;
        lock (chatRoomLock)
        {
            if (chatRooms.TryGetValue(roomId, out var room))
            {
                RemoveFromRoom(session.CharacterId);
                room.Members.Add(session.CharacterId);
                chatRoomByChar[session.CharacterId] = roomId;
                ok = true;
            }
            else
            {
                ok = false;
            }
        }

        await session.Client.SendPacket(ChatRoomPacketWriter.RoomResult(
            ChatRoomSubJoin,
            ok ? ChatRoomPacketWriter.Succeeded : ChatRoomPacketWriter.Failed,
            roomId));
    }

    private async Task HandleLeaveAsync(UserSession session)
    {
        bool ok;
        lock (chatRoomLock)
        {
            ok = chatRoomByChar.ContainsKey(session.CharacterId);
            RemoveFromRoom(session.CharacterId);
        }

        await session.Client.SendPacket(ChatRoomPacketWriter.Result(
            ChatRoomSubLeave,
            ok ? ChatRoomPacketWriter.Succeeded : ChatRoomPacketWriter.Failed));
    }

    private async Task HandleSayAsync(UserSession session, Packet packet)
    {
        var text = packet.RemainingBytes >= 1 ? packet.ReadSByteString() : string.Empty;
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
            return;

        int roomId;
        int[] members;
        lock (chatRoomLock)
        {
            if (!chatRoomByChar.TryGetValue(session.CharacterId, out roomId) ||
                !chatRooms.TryGetValue(roomId, out var room))
                return;
            members = room.Members.ToArray();
        }

        var broadcast = ChatRoomPacketWriter.Say(
            ChatRoomSubSayBroadcast, roomId, session.Name, text);

        foreach (var charId in members)
        {
            var target = sessionManager.GetByCharacterId(charId);
            if (target != null)
                await target.Client.SendPacket(broadcast);
        }
    }

    // Caller must hold chatRoomLock. Removes the character from whatever room they are in and
    // deletes the room if it becomes empty.
    private static void RemoveFromRoom(int charId)
    {
        if (!chatRoomByChar.TryGetValue(charId, out var roomId))
            return;
        chatRoomByChar.Remove(charId);
        if (chatRooms.TryGetValue(roomId, out var room))
        {
            room.Members.Remove(charId);
            if (room.Members.Count == 0)
                chatRooms.Remove(roomId);
        }
    }
}
