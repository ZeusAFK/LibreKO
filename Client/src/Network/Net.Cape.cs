using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte CapeOpBuy = 0, CapeOpTicket = 1;

    public const short CapeChanged = 1;

    public event Action<bool, int, int, int, int, int>? CapeResultEvent;
    public event Action? CapeResetEvent;
    private bool _capePending;

    private void HandleCape(Packet p)
    {
        if (!_capePending || !CapeWire.TryRead(p, out var reply)) return;
        if (!reply.Changed)
        {
            _capePending = false;
            CapeResultEvent?.Invoke(false, reply.Result, 0, 0, 0, 0);
            return;
        }

        if (!MyClan.InClan || reply.ClanId != MyClan.ClanId)
        {
            ResetCape();
            return;
        }
        _capePending = false;
        CapeResultEvent?.Invoke(true, reply.ClanId, reply.CapeId, reply.R, reply.G, reply.B);
    }

    public bool SendCapeBuy(byte op, int capeId, byte r, byte g, byte b)
    {
        if (_capePending || !CapeWire.IsRequest(op, capeId)) return false;
        _capePending = true;
        var p = new Packet(GameOpcodes.GS_CAPE);
        p.WriteByte(op);
        p.WriteShort((short)capeId);
        p.WriteInt(r | (g << 8) | (b << 16));
        _conn.Send(p);
        return true;
    }

    private void ResetCape()
    {
        _capePending = false;
        CapeResetEvent?.Invoke();
    }
}
