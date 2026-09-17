using System;

namespace LibreKO.Network;

public partial class Net
{
    public const byte ClanPremiumSubQuery = 1;
    public const byte ClanPremiumSubChange = 2;

    public event Action<bool>? ClanPremiumEvent;

    public bool ClanPremiumActive { get; private set; }
    public bool ClanPremiumKnown { get; private set; }

    private void HandleClanPremium(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        p.ReadByte();
        int result = p.RemainingBytes >= 1 ? p.ReadByte() : 0;

        ClanPremiumActive = result == 1;
        ClanPremiumKnown = true;
        ClanPremiumEvent?.Invoke(ClanPremiumActive);
    }

    public void SendClanPremiumQuery()
    {
        var p = new Packet(GameOpcodes.GS_CLAN_PREMIUM);
        p.WriteByte(ClanPremiumSubQuery);
        _conn.Send(p);
    }
}
