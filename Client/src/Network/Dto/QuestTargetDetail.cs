namespace LibreKO.Network;

public sealed record QuestTargetDetail(
    int QuestId, int ZoneId, int X, int Z, string QuestTitle, string Target, string About, string Where)
{
    public const int NoCoordinate = -1;

    public bool HasCoordinates => X >= 0 && Z >= 0;

    public static QuestTargetDetail Read(Packet packet)
    {
        var version = packet.ReadByte();
        var questId = packet.ReadShort();
        var zone = packet.ReadInt();
        var x = packet.ReadInt();
        var z = packet.ReadInt();
        var questTitle = packet.ReadUtf8String();
        var target = packet.ReadUtf8String();
        var about = packet.ReadUtf8String();
        var where = packet.ReadUtf8String();
        if (version != 1 || questId < 0 || zone < 0 || x < NoCoordinate || z < NoCoordinate
            || packet.RemainingBytes != 0)
            throw new InvalidDataException("Invalid quest target.");
        return new QuestTargetDetail(questId, zone, x, z, questTitle, target, about, where);
    }
}

public sealed record QuestReceipt(int QuestId, QuestReceiptEntry[] Granted)
{
    public const int MaxEntries = 5;

    public static QuestReceipt Read(Packet packet)
    {
        var version = packet.ReadByte();
        var questId = packet.ReadShort();
        var count = packet.ReadByte();
        if (version != 1 || questId <= 0 || count > MaxEntries || packet.RemainingBytes != count * 8)
            throw new InvalidDataException("Invalid quest receipt.");
        var granted = new QuestReceiptEntry[count];
        for (var index = 0; index < count; index++)
        {
            var itemId = packet.ReadInt();
            var amount = packet.ReadInt();
            if (itemId <= 0 || amount <= 0)
                throw new InvalidDataException("Invalid quest receipt entry.");
            granted[index] = new QuestReceiptEntry(itemId, amount);
        }
        return new QuestReceipt(questId, granted);
    }
}

public sealed record QuestReceiptEntry(int ItemId, int Count);
