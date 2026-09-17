using LibreKO.Domain;

namespace LibreKO.Network;

public enum QuestViewState : byte
{
    Locked,
    Available,
    InProgress,
    Claimable,
    Completed,
}

public sealed record QuestTransfer(bool Take, byte Kind, int ItemId, int Count, int RentalHours)
{
    public int DisplayItemId => Kind switch
    {
        1 => QuestData.CoinItemId,
        2 => QuestData.ExpItemId,
        3 => QuestData.LadderPointItemId,
        4 or 5 => QuestData.JobChangeItemId,
        _ => ItemId
    };
}

public enum QuestPageKind : byte
{
    Conversation,
    Quest,
}

public sealed record QuestView(int QuestId, int NpcId, int ZoneId, bool Open, bool CanAccept, bool CanClaim,
    bool Daily, QuestViewState State, long NextReset, string Title, string Journal, string Dialogue,
    QuestObjectives Objectives, ushort[] Counts, QuestTransfer[] Transfers, string[] Topics,
    bool Notification = false, QuestPageKind Page = QuestPageKind.Quest, bool AutoAccepted = false)
{
    public QuestTransfer[] Options { get; init; } = [];

    public bool JournalClaim { get; init; }

    public string StateLabel => State switch
    {
        QuestViewState.Available => "Available",
        QuestViewState.InProgress => "In progress",
        QuestViewState.Claimable => "Ready to claim",
        QuestViewState.Completed => Daily ? "Completed today" : "Completed",
        _ => "Unavailable"
    };

    public string StandingObjective => State switch
    {
        QuestViewState.Claimable => "Ready to turn in.",
        _ => Journal.Length > 0 ? Journal : "Follow the quest instructions."
    };

    public static QuestView Read(Packet packet)
    {
        var version = packet.ReadByte();
        var id = packet.ReadShort();
        var npc = packet.ReadInt();
        var zone = packet.ReadInt();
        var flags = packet.ReadByte();
        var state = (QuestViewState)packet.ReadByte();
        var page = (QuestPageKind)packet.ReadByte();
        var reset = packet.ReadLong();
        if (version != 2 || id <= 0 || npc < 0 || zone < 0 || flags > 127 || state > QuestViewState.Completed
            || ((flags & 64) != 0 && ((flags & 32) == 0 || state != QuestViewState.Claimable))
            || page > QuestPageKind.Quest || reset is < 0 or > 253402300799
            || ((flags & 16) != 0 && ((flags & 1) == 0 || (flags & 6) != 0
                || state is not (QuestViewState.Available or QuestViewState.InProgress or QuestViewState.Claimable or QuestViewState.Completed)
                || state is (QuestViewState.InProgress or QuestViewState.Completed) && (flags & 32) == 0))
            || ((flags & 6) != 0 && ((flags & 1) == 0 || page != QuestPageKind.Quest))
            || ((flags & 32) != 0 && (flags & 6) != 0)
            || ((flags & 2) != 0 && state != QuestViewState.Available)
            || ((flags & 4) != 0 && state != QuestViewState.Claimable))
            throw new InvalidDataException("Invalid quest view header.");
        var title = packet.ReadUtf8String();
        var journal = packet.ReadUtf8String();
        var dialogue = packet.ReadUtf8String();
        var rule = packet.ReadByte();
        var count = packet.ReadByte();
        if (rule > 1 || count > 4)
            throw new InvalidDataException("Invalid quest view objectives.");
        var groups = new QuestKillGroup[count];
        var counts = new ushort[count];
        for (var index = 0; index < count; index++)
        {
            int target = (ushort)packet.ReadUShort();
            counts[index] = (ushort)packet.ReadUShort();
            var monsters = new int[packet.ReadByte()];
            if (target is 0 or > short.MaxValue || counts[index] > target || monsters.Length is < 1 or > 4)
                throw new InvalidDataException("Invalid quest view objective.");
            for (var m = 0; m < monsters.Length; m++)
            {
                monsters[m] = packet.ReadInt();
                if (monsters[m] is <= 0 or > short.MaxValue)
                    throw new InvalidDataException("Invalid quest view monster.");
            }
            var name = packet.ReadUtf8String();
            var hasTarget = packet.ReadByte();
            if (hasTarget > 1)
                throw new InvalidDataException("Invalid quest view objective target.");
            groups[index] = new QuestKillGroup(target, monsters, name, hasTarget == 1);
        }
        var transfers = ReadTransfers(packet, false);
        var options = ReadTransfers(packet, true);
        var topics = new string[(ushort)packet.ReadUShort()];
        if (topics.Length > 256 || (topics.Length > 0 && (flags & 1) == 0))
            throw new InvalidDataException("Invalid quest view topics.");
        for (var index = 0; index < topics.Length; index++)
            topics[index] = packet.ReadUtf8String();
        if (packet.RemainingBytes != 0)
            throw new InvalidDataException("Unexpected quest view data.");
        return new QuestView(id, npc, zone, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0,
            (flags & 8) != 0, state, reset, title, journal, dialogue,
            new QuestObjectives(id, rule == 1, groups), counts, transfers, topics, (flags & 16) != 0, page, (flags & 32) != 0)
            { Options = options, JournalClaim = (flags & 64) != 0 };
    }

    private static QuestTransfer[] ReadTransfers(Packet packet, bool options)
    {
        var count = (ushort)packet.ReadUShort();
        if (count > packet.RemainingBytes / 14 || (options && count == 1))
            throw new InvalidDataException("Invalid quest view transfers.");
        var transfers = new QuestTransfer[count];
        for (var index = 0; index < count; index++)
        {
            var take = packet.ReadByte();
            var kind = packet.ReadByte();
            var item = packet.ReadInt();
            var amount = packet.ReadInt();
            var hours = packet.ReadInt();
            if (take > 1 || kind > 5 || amount <= 0 || hours < 0 || (kind == 0 ? item <= 0 : item != 0)
                || (take == 1 && kind is 2 or 4 or 5) || (options && (take == 1 || kind is 4 or 5)))
                throw new InvalidDataException("Invalid quest view transfer.");
            transfers[index] = new QuestTransfer(take == 1, kind, item, amount, hours);
        }
        return transfers;
    }
}
