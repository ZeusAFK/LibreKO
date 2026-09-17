using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<FriendEntry>>? FriendListEvent;

    public event Action<byte, string, FriendEntry>? FriendAddResultEvent;

    public event Action<byte, string>? FriendRemoveResultEvent;

    private void HandleFriendProcess(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case FriendSubList:
            {
                if (p.RemainingBytes < 2) return;
                int count = p.ReadUShort();
                _friendList.Clear();
                for (int i = 0; i < count && p.RemainingBytes >= 7; i++)
                    _friendList.Add(ReadFriendStatus(p));
                FriendListEvent?.Invoke(new List<FriendEntry>(_friendList));
                break;
            }
            case FriendSubDetails:
            {
                if (p.RemainingBytes < 2) return;
                int count = p.ReadUShort();
                for (int i = 0; i < count && p.RemainingBytes >= 7; i++)
                {
                    string name = p.ReadString().Trim();
                    byte level = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    short cls = p.RemainingBytes >= 2 ? p.ReadShort() : (short)0;
                    byte nation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    byte zone = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;

                    for (int j = 0; j < _friendList.Count; j++)
                    {
                        if (!string.Equals(_friendList[j].Name, name, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var e = _friendList[j];
                        e.Level = level; e.Class = cls; e.Nation = nation; e.ZoneId = zone;
                        _friendList[j] = e;
                        break;
                    }
                }
                FriendListEvent?.Invoke(new List<FriendEntry>(_friendList));
                break;
            }
            case 3:
            {
                byte code = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)1;
                string name = p.RemainingBytes > 0 ? p.ReadSByteString().Trim() : "";
                var status = ReadFriendStatus(p);
                FriendAddResultEvent?.Invoke(code, name.Length > 0 ? name : status.Name, status);
                break;
            }
            case 4:
            {
                byte code = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)1;
                string name = p.RemainingBytes > 0 ? p.ReadSByteString().Trim() : "";
                ReadFriendStatus(p);
                FriendRemoveResultEvent?.Invoke(code, name);
                break;
            }
        }
    }

    private const byte FriendSubList = 1;
    private const byte FriendSubDetails = 6;

    private readonly List<FriendEntry> _friendList = new();

    private static FriendEntry ReadFriendStatus(Packet p)
    {
        string name = p.RemainingBytes > 0 ? p.ReadString().Trim() : "";
        int charId = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
        byte status = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
        return new FriendEntry { Name = name, CharId = charId, Status = status };
    }

    public void SendFriendListRequest()
    {
        var p = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendFriendAdd(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (name.Length > 20) name = name.Substring(0, 20);
        var p = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        p.WriteByte(3);
        p.WriteSByteString(name);
        _conn.Send(p);
    }

    public void SendFriendRemove(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        if (name.Length > 20) name = name.Substring(0, 20);
        var p = new Packet(GameOpcodes.GS_FRIEND_PROCESS);
        p.WriteByte(4);
        p.WriteSByteString(name);
        _conn.Send(p);
    }
}
