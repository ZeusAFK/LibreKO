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
        if (PremiumWire.ReadList(p) is not { } list) return;
        var (accountStatus, premiumType, premiumHours) = list;
        premium.Apply(accountStatus, premiumType, premiumHours, PremiumWire.ReadBonuses(p));

        premium.RaisePremium(accountStatus, premiumType, premiumHours);
    }
}
