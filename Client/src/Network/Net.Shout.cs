using System;

namespace LibreKO.Network;

public partial class Net
{
    public const int LogosShoutItem = 800075000;
    public const int LogosShoutMaxLen = 128;

    public const int ShoutSubRegisterResult = 1;
    public const int ShoutRegisterAccepted = 1;
    public const int ShoutRegisterNoItem = -2;
    public const int ShoutRegisterRetryLater = -3;
    public const int ShoutRegisterChatRestricted = -5;
    public const int ShoutRegisterLevelTooLow = -6;

    public event Action<string, byte, byte, byte, int>? ShoutEvent;

    public event Action<bool, string, int, int>? ShoutUpgradeEvent;

    public event Action<string, int, byte>? ShoutRareItemEvent;

    public event Action<int>? ShoutResultEvent;

    private void HandleShout(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        int direction = p.ReadByte();

        if (direction == ShoutSubRegisterResult)
        {
            int result = (sbyte)p.ReadByte();
            if (result == ShoutRegisterAccepted && p.RemainingBytes >= 1) p.ReadByte();
            ShoutResultEvent?.Invoke(result);
            return;
        }

        int kind = p.ReadByte();

        switch (kind)
        {
            case 1:
            {
                if (p.RemainingBytes < 4) return;
                byte r = p.ReadByte();
                byte g = p.ReadByte();
                byte b = p.ReadByte();
                p.ReadByte();
                string msg = p.ReadSByteString();
                int rank = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                ShoutEvent?.Invoke(msg, r, g, b, rank);
                break;
            }
            case 4:
            {
                string finder = p.ReadSByteString();
                int foundItemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                if (p.RemainingBytes >= 1) p.ReadByte();
                byte nation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                ShoutRareItemEvent?.Invoke(finder, foundItemId, nation);
                break;
            }
            case 5:
            {
                if (p.RemainingBytes < 1) return;
                bool ok = p.ReadByte() == 1;
                string name = p.ReadSByteString();
                int itemId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                if (p.RemainingBytes >= 1) p.ReadByte();
                byte upgradeNation = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                ShoutUpgradeEvent?.Invoke(ok, name, itemId, upgradeNation);
                break;
            }
        }
    }

    public void SendLogoShout(string message, byte r = 0xE0, byte g = 0xC0, byte b = 0x60)
    {
        if (string.IsNullOrEmpty(message)) return;
        if (message.Length > LogosShoutMaxLen) message = message.Substring(0, LogosShoutMaxLen);

        var p = new Packet(GameOpcodes.GS_LOGOSSHOUT);
        p.WriteByte(0);
        p.WriteByte(r);
        p.WriteByte(g);
        p.WriteByte(b);
        p.WriteByte(0);
        p.WriteSByteString(message);
        _conn.Send(p);
    }
}
