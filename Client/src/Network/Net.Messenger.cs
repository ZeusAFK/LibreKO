using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<List<MessengerBuddy>>? MessengerListEvent;
    public event Action<string, byte>? MessengerSendResultEvent;

    private void HandleMessenger(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        if (sub == 1)
        {
            var list = new List<MessengerBuddy>();
            int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
            for (int i = 0; i < count && p.RemainingBytes >= 5; i++)
            {
                int charId = p.ReadInt();
                string name = p.ReadSByteString().Trim();
                byte online = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                list.Add(new MessengerBuddy { CharId = charId, Name = name, Online = online != 0 });
            }
            MessengerListEvent?.Invoke(list);
        }
        else if (sub == 2)
        {
            byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
            string toName = p.RemainingBytes > 0 ? p.ReadSByteString().Trim() : "";
            MessengerSendResultEvent?.Invoke(toName, result);
        }
    }

    public void SendMessengerList()
    {
        var p = new Packet(GameOpcodes.GS_MESSENGER);
        p.WriteByte(1);
        _conn.Send(p);
    }

    public void SendMessengerMessage(string toName, string text)
    {
        if (string.IsNullOrEmpty(toName) || string.IsNullOrEmpty(text)) return;
        if (toName.Length > 20) toName = toName.Substring(0, 20);
        if (text.Length > 128) text = text.Substring(0, 128);
        var p = new Packet(GameOpcodes.GS_MESSENGER);
        p.WriteByte(2);
        p.WriteSByteString(toName);
        p.WriteSByteString(text);
        _conn.Send(p);
    }
}

public struct MessengerBuddy
{
    public int CharId;
    public string Name;
    public bool Online;
}
