namespace LibreKO.Network;

public sealed record QuestKillGroup(int Count, int[] Monsters, string? Name = null, bool HasTarget = false);

public sealed record QuestObjectives(int QuestId, bool AnyWillDo, QuestKillGroup[] Groups)
{
    public static QuestObjectives Read(Packet packet)
    {
        var questId = packet.ReadShort();
        var rule = packet.ReadByte();
        var groupCount = packet.ReadByte();
        if (questId <= 0 || rule > 1 || groupCount is < 1 or > 4)
            throw new InvalidDataException("Invalid quest objectives header.");

        var groups = new QuestKillGroup[groupCount];
        for (var index = 0; index < groups.Length; index++)
        {
            var count = packet.ReadUShort();
            var monsterCount = packet.ReadByte();
            if (count == 0 || count > short.MaxValue || monsterCount is < 1 or > 4)
                throw new InvalidDataException("Invalid quest objective.");
            var monsters = new int[monsterCount];
            for (var monster = 0; monster < monsters.Length; monster++)
            {
                monsters[monster] = packet.ReadInt();
                if (monsters[monster] is <= 0 or > short.MaxValue)
                    throw new InvalidDataException("Invalid quest monster.");
            }
            groups[index] = new QuestKillGroup(count, monsters);
        }
        if (packet.RemainingBytes != 0)
            throw new InvalidDataException("Unexpected quest objective data.");
        return new QuestObjectives(questId, rule == 1, groups);
    }

    public (int Current, int Target) Progress(IReadOnlyList<ushort> counts)
    {
        var current = 0;
        var target = 0;
        for (var index = 0; index < Groups.Length; index++)
        {
            var need = Groups[index].Count;
            var done = Math.Min(index < counts.Count ? counts[index] : 0, need);
            if (AnyWillDo)
            {
                if (target == 0 || (long)done * target > (long)current * need)
                    (current, target) = (done, need);
            }
            else
            {
                current += done;
                target += need;
            }
        }
        return (current, target);
    }
}
