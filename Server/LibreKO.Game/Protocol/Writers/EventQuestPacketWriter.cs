using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class EventQuestPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public readonly record struct Entry(int QuestId, string Title, bool Accepted, bool Claimable);

    public static Packet QuestList(byte sub, IReadOnlyCollection<Entry> quests)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)quests.Count);

        foreach (var quest in quests)
        {
            packet.WriteInt(quest.QuestId);
            packet.WriteSByteString(quest.Title);
            packet.WriteByte(quest.Accepted ? Succeeded : Failed);
            packet.WriteByte(quest.Claimable ? Succeeded : Failed);
        }

        return packet;
    }

    public static Packet Result(byte sub, byte result, int questId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(questId);
        return packet;
    }

    private static Packet Sub(byte sub)
    {
        var packet = new Packet(GameOpcodes.GS_EVENT_QUEST);
        packet.WriteByte(sub);
        return packet;
    }
}
