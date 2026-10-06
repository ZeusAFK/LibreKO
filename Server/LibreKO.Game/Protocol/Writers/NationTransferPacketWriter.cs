using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed record NationTransferCandidate(short Slot, string Name, byte Race, AccountNation Nation, short Class, byte Face, int Hair);

public static class NationTransferPacketWriter
{
    public const byte WarStatus = 1;
    public const byte OpenBox = 2;
    public const byte Submit = 3;

    public const byte Failed = 0;
    public const byte Accepted = 1;
    public const byte InClan = 2;
    public const byte IsKing = 3;
    public const byte WrongCharacter = 5;
    public const byte NoCharacter = 6;
    public const byte NoItem = 7;
    public const byte WarRunning = 8;

    public static Packet Result(byte sub, byte result)
    {
        var packet = new Packet(GameOpcodes.GS_NATION_TRANSFER);
        packet.WriteByte(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet DuringWar(int karusScore, int elmoradScore)
    {
        var packet = Result(WarStatus, WarRunning);
        packet.WriteByte((byte)Math.Clamp(karusScore, 0, byte.MaxValue));
        packet.WriteByte((byte)Math.Clamp(elmoradScore, 0, byte.MaxValue));
        return packet;
    }

    public static Packet Candidates(IReadOnlyList<NationTransferCandidate> candidates)
    {
        var packet = Result(OpenBox, Accepted);
        packet.WriteByte((byte)candidates.Count);
        foreach (var candidate in candidates)
        {
            packet.WriteShort(candidate.Slot);
            packet.WriteString(candidate.Name);
            packet.WriteByte(candidate.Race);
            packet.WriteByte((byte)candidate.Nation);
            packet.WriteShort(candidate.Class);
            packet.WriteByte(candidate.Face);
            packet.WriteInt(candidate.Hair);
        }
        return packet;
    }
}
