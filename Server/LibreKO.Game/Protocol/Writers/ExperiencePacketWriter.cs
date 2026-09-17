using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class ExperiencePacketWriter
{

    public static Packet Current(long currentExperience) => Sub(ExperienceSubOpcode.CurrentExperience, currentExperience);

    private static Packet Sub(ExperienceSubOpcode sub, long value)
    {
        var packet = new Packet(GameOpcodes.GS_EXP_CHANGE);
        packet.WriteByte((byte)sub);
        packet.WriteLong(value);
        return packet;
    }
}
