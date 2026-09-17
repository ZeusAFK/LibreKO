using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class DuelPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const int NoDuelId = 0;

    public readonly record struct Listing(int DuelId, string CreatorName, int Stake, bool Full);

    public static Packet DuelList(byte sub, IReadOnlyCollection<Listing> duels)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)duels.Count);

        foreach (var duel in duels)
        {
            packet.WriteInt(duel.DuelId);
            packet.WriteSByteString(duel.CreatorName);
            packet.WriteInt(duel.Stake);
            packet.WriteByte(duel.Full ? Succeeded : Failed);
        }

        return packet;
    }

    public static Packet Result(byte sub, byte result, int duelId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(duelId);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_DUEL);
        packet.WriteByte(sub);
        return packet;
    }
}
