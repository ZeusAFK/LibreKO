namespace LibreKO.Network;

public sealed record QuestStrings(int QuestId, string Title, string Journal)
{
    public static List<QuestStrings> ReadList(Packet packet)
    {
        var count = packet.ReadShort();
        if (count < 0)
            throw new InvalidDataException("Invalid quest text count.");

        var texts = new List<QuestStrings>(count);
        for (var index = 0; index < count; index++)
        {
            var questId = packet.ReadShort();
            var title = packet.ReadUtf8String();
            var journal = packet.ReadUtf8String();
            if (questId <= 0)
                throw new InvalidDataException("Invalid quest id in quest text.");
            texts.Add(new QuestStrings(questId, title, journal));
        }
        if (packet.RemainingBytes != 0)
            throw new InvalidDataException("Unexpected quest text data.");
        return texts;
    }
}
