using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class CaptchaPacketWriter
{
    public static Packet Skip()
    {
        var packet = new Packet(GameOpcodes.GS_CAPTCHA);
        packet.WriteByte(1);
        packet.WriteByte(2);
        packet.WriteByte(1);
        packet.WriteShort(0);
        return packet;
    }
}
