using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<ChatRoomEntry>>? ChatRoomListEvent;
    public event Action<int, bool>? ChatRoomCreateEvent;
    public event Action<int, bool>? ChatRoomJoinEvent;
    public event Action<bool>? ChatRoomLeaveEvent;
    public event Action<int, string, string>? ChatRoomSayEvent;

    private void HandleChatRoom(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 1:
            {
                var list = new List<ChatRoomEntry>();
                int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                {
                    int roomId = p.ReadInt();
                    string name = p.ReadSByteString();
                    int members = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    list.Add(new ChatRoomEntry { RoomId = roomId, Name = name, MemberCount = members });
                }
                ChatRoomListEvent?.Invoke(list);
                break;
            }
            case 2:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int roomId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                ChatRoomCreateEvent?.Invoke(roomId, ok);
                break;
            }
            case 3:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int roomId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                ChatRoomJoinEvent?.Invoke(roomId, ok);
                break;
            }
            case 4:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                ChatRoomLeaveEvent?.Invoke(ok);
                break;
            }
            case 6:
            {
                int roomId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                string sender = p.ReadSByteString();
                string text = p.ReadSByteString();
                ChatRoomSayEvent?.Invoke(roomId, sender, text);
                break;
            }
        }
    }

    public void SendChatRoomList()
    {
        var p = new Packet(GameOpcodes.GS_CHATROOM);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendChatRoomCreate(string name)
    {
        var p = new Packet(GameOpcodes.GS_CHATROOM);
        p.WriteByte(2);
        p.WriteSByteString(name ?? string.Empty);
        _conn.Send(p);
    }

    public void SendChatRoomJoin(int roomId)
    {
        var p = new Packet(GameOpcodes.GS_CHATROOM);
        p.WriteByte(3);
        p.WriteInt(roomId);
        _conn.Send(p);
    }

    public void SendChatRoomLeave()
    {
        var p = new Packet(GameOpcodes.GS_CHATROOM);
        p.WriteByte(4);
        _conn.Send(p);
    }

    public void SendChatRoomSay(string text)
    {
        var p = new Packet(GameOpcodes.GS_CHATROOM);
        p.WriteByte(5);
        p.WriteSByteString(text ?? string.Empty);
        _conn.Send(p);
    }
}

public struct ChatRoomEntry
{
    public int RoomId;
    public string Name;
    public int MemberCount;
}
