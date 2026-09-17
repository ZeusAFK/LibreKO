using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public static class StealthPacketWriter
{
    private const byte Off = 0;
    private const byte On = 1;

    public static Packet Sight(short radius) => Build(On, radius);

    public static Packet NoSight() => Build(Off, 0);

    private static Packet Build(byte state, short radius)
    {
        var packet = new Packet(GameOpcodes.GS_STEALTH);
        packet.WriteByte(state);
        packet.WriteShort(radius);
        return packet;
    }
}
