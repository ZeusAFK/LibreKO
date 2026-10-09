using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public enum RebirthResult : short
{
    Success = 1,
    NoQualification = -2,
    LevelTooLow = -3,
    ExperienceNotFull = -5,
    Unavailable = -7,
}

public static class RebirthPacketWriter
{
    public static Packet Result(RebirthResult result)
    {
        var packet = new Packet(GameOpcodes.GS_CLASS_CHANGE);
        packet.WriteByte((byte)ClassChangeSubOpcode.RebirthStatChange);
        packet.WriteShort((short)result);
        return packet;
    }
}
