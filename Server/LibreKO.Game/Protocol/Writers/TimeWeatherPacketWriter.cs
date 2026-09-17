using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class TimeWeatherPacketWriter
{
    public static Packet GameTime(short year, short month, short day, short hour, short minute)
    {
        var packet = new Packet(GameOpcodes.GS_TIME);
        packet.WriteShort(year);
        packet.WriteShort(month);
        packet.WriteShort(day);
        packet.WriteShort(hour);
        packet.WriteShort(minute);
        return packet;
    }

    public static Packet Weather(byte type, ushort amount)
    {
        var packet = new Packet(GameOpcodes.GS_WEATHER);
        packet.WriteByte(type);
        packet.WriteUShort(amount);
        return packet;
    }
}
