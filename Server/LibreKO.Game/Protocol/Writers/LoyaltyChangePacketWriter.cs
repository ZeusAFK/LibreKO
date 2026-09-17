using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class LoyaltyChangePacketWriter
{

    public int Loyalty { get; init; }
    public int MonthlyLoyalty { get; init; }
    public int RivalPoints { get; init; }
    public int RivalKills { get; init; }

    public static Packet Totals(int loyalty, int monthlyLoyalty) =>
        new LoyaltyChangePacketWriter
        {
            Loyalty = loyalty,
            MonthlyLoyalty = monthlyLoyalty,
        }.Build();

    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_LOYALTY_CHANGE);
        packet.WriteByte((byte)LoyaltySubOpcode.Totals);
        packet.WriteInt(Loyalty);
        packet.WriteInt(MonthlyLoyalty);
        packet.WriteInt(RivalPoints);
        packet.WriteInt(RivalKills);
        return packet;
    }
}
