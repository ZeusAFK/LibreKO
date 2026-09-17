using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int, bool>? HelmetEvent;

    public bool HelmetHidden { get; private set; }

    public bool CospreHelmetHidden { get; private set; }

    public event System.Action<int, bool>? CospreHelmetEvent;

    private void HandleHelmet(Packet p)
    {
        if (p.RemainingBytes < 6) return;
        bool hidden = p.ReadByte() != 0;
        bool cospreHidden = p.ReadByte() != 0;
        int charId = p.ReadInt();
        if (charId == LastEnter.CharId)
        {
            HelmetHidden = hidden;
            CospreHelmetHidden = cospreHidden;
        }
        HelmetEvent?.Invoke(charId, hidden);
        CospreHelmetEvent?.Invoke(charId, cospreHidden);
    }

    public void SendHelmetToggle(bool hide)
    {
        var p = new Packet(GameOpcodes.GS_HELMET);
        p.WriteByte((byte)(hide ? 1 : 0));
        p.WriteByte((byte)(hide ? 1 : 0));
        _conn.Send(p);
    }
}
