namespace LibreKO.Network.Readers;

internal sealed class PremiumReader : IPacketReader
{
    private readonly PremiumSession premium;

    public PremiumReader(PremiumSession premium)
    {
        this.premium = premium;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_PREMIUM, HandlePremium);
    }

    private void HandlePremium(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        switch ((PremiumSubOpcode)p.ReadByte())
        {
            case PremiumSubOpcode.List:
                premium.Apply(PremiumWire.ReadList(p), PremiumWire.ReadBonuses(p));
                premium.RaisePremium(premium.PremiumAccountStatus, premium.PremiumType, premium.PremiumHours);
                break;
            case PremiumSubOpcode.Switch:
                if (PremiumWire.ReadSwitch(p) is { } reply) premium.RaiseSwitch(reply);
                break;
        }
    }
}
