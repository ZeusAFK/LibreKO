using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class CollectionRacePacketWriter
{
    public const byte SubState = 1;
    public const byte SubProgress = 2;
    public const byte SubCompleted = 3;
    public const byte SubClose = 4;

    public readonly record struct TargetInfo(int ProtoId, int TargetCount, int CurrentCount, string Name);
    public readonly record struct RewardInfo(int ItemId, int ItemCount, string Name, byte Rate = 100);

    public static Packet State(
        int eventIndex,
        string eventName,
        byte zoneId,
        int remainingSeconds,
        TargetInfo t1,
        TargetInfo t2,
        TargetInfo t3,
        int enemyTarget,
        int enemyCurrent,
        bool isCompleted,
        IReadOnlyList<RewardInfo> rewards)
    {
        var packet = new Packet(GameOpcodes.GS_COLLECTION_RACE);
        packet.WriteByte(SubState);
        packet.WriteInt(eventIndex);
        packet.WriteSByteString(eventName);
        packet.WriteByte(zoneId);
        packet.WriteInt(remainingSeconds);

        packet.WriteInt(t1.ProtoId);
        packet.WriteInt(t1.TargetCount);
        packet.WriteInt(t1.CurrentCount);
        packet.WriteSByteString(t1.Name);

        packet.WriteInt(t2.ProtoId);
        packet.WriteInt(t2.TargetCount);
        packet.WriteInt(t2.CurrentCount);
        packet.WriteSByteString(t2.Name);

        packet.WriteInt(t3.ProtoId);
        packet.WriteInt(t3.TargetCount);
        packet.WriteInt(t3.CurrentCount);
        packet.WriteSByteString(t3.Name);

        packet.WriteInt(enemyTarget);
        packet.WriteInt(enemyCurrent);

        packet.WriteByte((byte)(isCompleted ? 1 : 0));

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

    public static Packet Progress(int t1Current, int t2Current, int t3Current, int enemyCurrent)
    {
        var packet = new Packet(GameOpcodes.GS_COLLECTION_RACE);
        packet.WriteByte(SubProgress);
        packet.WriteInt(t1Current);
        packet.WriteInt(t2Current);
        packet.WriteInt(t3Current);
        packet.WriteInt(enemyCurrent);
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
