using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class BountyPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const byte StandingOpen = 0;
    public const byte StandingOwn = 1;
    public const byte StandingEarned = 2;

    public readonly record struct Entry(int BountyId, string Target, string Poster, int Reward, byte Standing);

    public static Packet BountyList(byte sub, IReadOnlyCollection<Entry> bounties)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)bounties.Count);

        foreach (var bounty in bounties)
        {
            packet.WriteInt(bounty.BountyId);
            packet.WriteSByteString(bounty.Target);
            packet.WriteSByteString(bounty.Poster);
            packet.WriteInt(bounty.Reward);
            packet.WriteByte(bounty.Standing);
        }

        return packet;
    }

    public static Packet Result(byte sub, byte result)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet BountyResult(byte sub, byte result, int bountyId)
    {
        var packet = Result(sub, result);
        packet.WriteInt(bountyId);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_BOUNTY);
        packet.WriteByte(sub);
        return packet;
    }
}
