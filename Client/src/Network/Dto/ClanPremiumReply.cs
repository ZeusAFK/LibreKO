namespace LibreKO.Network;

public readonly record struct ClanPremiumReply(bool Active, uint SecondsLeft)
{
    public static ClanPremiumReply? Read(Packet p)
    {
        if (p.RemainingBytes < 1) return null;
        p.ReadByte();
        bool active = p.RemainingBytes >= 1 && (ClanPremiumState)p.ReadByte() == ClanPremiumState.Active;
        return new ClanPremiumReply(active, p.RemainingBytes >= sizeof(uint) ? p.ReadUInt() : 0u);
    }
}
