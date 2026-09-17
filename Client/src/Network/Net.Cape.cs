using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte CapeOpBuy = 0, CapeOpTicket = 1;

    public const short CapeChanged = 1;

    public event Action<bool, int, int, int, int, int>? CapeResultEvent;

    private void HandleCape(Packet p)
    {
        int result = p.RemainingBytes >= 2 ? p.ReadShort() : -1;
        if (result != CapeChanged)
        {
            CapeResultEvent?.Invoke(false, result, 0, 0, 0, 0);
            return;
        }

        int clanId = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
        if (p.RemainingBytes >= 2) p.ReadShort();
        int capeId = p.RemainingBytes >= 2 ? p.ReadShort() : -1;
        int colour = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
        CapeResultEvent?.Invoke(
            true, clanId, capeId, colour & 0xFF, (colour >> 8) & 0xFF, (colour >> 16) & 0xFF);
    }

    public void SendCapeBuy(byte op, int capeId, byte r, byte g, byte b)
    {
        var p = new Packet(GameOpcodes.GS_CAPE);
        p.WriteByte(op);
        p.WriteShort((short)capeId);
        p.WriteInt(r | (g << 8) | (b << 16));
        _conn.Send(p);
    }
}
