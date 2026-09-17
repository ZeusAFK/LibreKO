using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class HelmetPacketWriter
{
    public bool HideHelmet { get; set; }
    public bool HideCospreHelmet { get; set; }
    public int CharacterId { get; set; }

    public static Packet Visibility(int characterId, bool hideHelmet, bool hideCospreHelmet) => new HelmetPacketWriter()
        {
            CharacterId = characterId,
            HideHelmet = hideHelmet,
            HideCospreHelmet = hideCospreHelmet,
        }.Build();
    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_HELMET);
        packet.WriteByte(HideHelmet ? (byte)1 : (byte)0);
        packet.WriteByte(HideCospreHelmet ? (byte)1 : (byte)0);
        packet.WriteInt(CharacterId);
        return packet;
    }
}
