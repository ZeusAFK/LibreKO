using System;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<int, int, int>? PremiumEvent;

    public int PremiumAccountStatus { get; private set; }
    public int PremiumType { get; private set; }
    public int PremiumHours { get; private set; }

    private void HandlePremium(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        int accountStatus = p.ReadByte();
        int premiumType = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
        int premiumHours = p.RemainingBytes >= 4 ? p.ReadInt() : 0;

        PremiumAccountStatus = accountStatus;
        PremiumType = premiumType;
        PremiumHours = premiumHours;

        PremiumEvent?.Invoke(accountStatus, premiumType, premiumHours);
    }

    public void SendPremiumRequest()
    {
        var p = new Packet(GameOpcodes.GS_PREMIUM);
        _conn.Send(p);
    }
}
