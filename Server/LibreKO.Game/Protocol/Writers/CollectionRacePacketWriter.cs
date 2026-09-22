using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class CollectionRacePacketWriter
{
    public const byte SubState = 1;
    public const byte SubProgress = 2;
    public const byte SubCompleted = 3;
    public const byte SubClose = 4;

    public readonly record struct ObjectiveInfo(CollectionRaceObjectiveKind Kind, int TargetId, int Count, int Current, string Name);
    public readonly record struct RewardInfo(int ItemId, int ItemCount, string Name, byte Rate);

    public static Packet State(
        int raceId,
        string name,
        byte zoneId,
        int remainingSeconds,
        bool isCompleted,
        IReadOnlyList<ObjectiveInfo> objectives,
        IReadOnlyList<RewardInfo> rewards)
    {
        var packet = new Packet(GameOpcodes.GS_COLLECTION_RACE);
        packet.WriteByte(SubState);
        packet.WriteInt(raceId);
        packet.WriteSByteString(name);
        packet.WriteByte(zoneId);
        packet.WriteInt(remainingSeconds);
        packet.WriteByte((byte)(isCompleted ? 1 : 0));

        packet.WriteByte((byte)objectives.Count);
        foreach (var o in objectives)
        {
            packet.WriteByte((byte)o.Kind);
            packet.WriteInt(o.TargetId);
            packet.WriteInt(o.Count);
            packet.WriteInt(o.Current);
            packet.WriteSByteString(o.Name);
        }

        packet.WriteByte((byte)rewards.Count);
        foreach (var r in rewards)
        {
            packet.WriteInt(r.ItemId);
            packet.WriteInt(r.ItemCount);
            packet.WriteSByteString(r.Name);
            packet.WriteByte(r.Rate);
        }

        return packet;
    }

    public static Packet Progress(int raceId, IReadOnlyList<int> currents)
    {
        var packet = new Packet(GameOpcodes.GS_COLLECTION_RACE);
        packet.WriteByte(SubProgress);
        packet.WriteInt(raceId);
        packet.WriteByte((byte)currents.Count);
        foreach (var current in currents)
            packet.WriteInt(current);
        return packet;
    }

    public static Packet Completed(string message)
    {
        var packet = new Packet(GameOpcodes.GS_COLLECTION_RACE);
        packet.WriteByte(SubCompleted);
        packet.WriteSByteString(message);
        return packet;
    }

    public static Packet Close()
    {
        var packet = new Packet(GameOpcodes.GS_COLLECTION_RACE);
        packet.WriteByte(SubClose);
        return packet;
    }
}
