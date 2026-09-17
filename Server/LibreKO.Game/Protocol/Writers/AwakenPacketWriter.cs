using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class AwakenPacketWriter
{
    public static Packet Effect(float effectScale, byte effectType, int effectId)
    {
        var packet = new Packet(GameOpcodes.GS_AWAKEN);
        packet.WriteFloat(effectScale);
        packet.WriteByte(effectType);
        packet.WriteInt(effectId);
        return packet;
    }
}
