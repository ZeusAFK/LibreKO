using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class DailyQuestPacketWriter
{
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public readonly record struct Entry(int QuestId, bool Available, bool Completed, string Title);

    public static Packet QuestList(DailyQuestSubOpcode sub, IReadOnlyCollection<Entry> quests)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)quests.Count);

        foreach (var quest in quests)
        {
            packet.WriteInt(quest.QuestId);
            packet.WriteByte(quest.Available ? Succeeded : Failed);
            packet.WriteByte(quest.Completed ? Succeeded : Failed);
            packet.WriteSByteString(quest.Title);
        }

        return packet;
    }

    public static Packet Result(DailyQuestSubOpcode sub, byte result, int questId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteInt(questId);
        return packet;
    }

    private static Packet Sub(DailyQuestSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_DAILY_QUEST);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
