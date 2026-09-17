using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public static class ClientSettingsPacketWriter
{
    public static Packet Language(GameLanguage language, bool accepted)
    {
        var packet = new Packet(GameOpcodes.GS_CLIENT_SETTINGS);
        packet.WriteByte(ClientSettingsPacketCoordinator.SubSetLanguage);
        packet.WriteByte((byte)(accepted ? 1 : 0));
        packet.WriteByte((byte)language);
        return packet;
    }
}
