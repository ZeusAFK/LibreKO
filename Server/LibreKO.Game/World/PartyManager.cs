namespace LibreKO.Game.World;

public class PartyGroup
{
    public const int MaxMembers = 8;

    public int Index { get; }
    public short[] MemberIds { get; } = new short[MaxMembers];

    public short TargetNumberId { get; set; } = -1;

    public short CommandLeaderId { get; set; } = -1;

    public bool IsCommandLeader(short charId)
        => charId == LeaderId || (CommandLeaderId > 0 && charId == CommandLeaderId);

    public PartyGroup(int index)
    {
        Index = index;
        for (int i = 0; i < MaxMembers; i++)
            MemberIds[i] = -1;
    }

    public int MemberCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < MaxMembers; i++)
                if (MemberIds[i] >= 0) count++;
            return count;
        }
    }

    public short LeaderId => MemberIds[0];

    public int FindEmptySlot()
    {
        for (int i = 0; i < MaxMembers; i++)
            if (MemberIds[i] < 0) return i;
        return -1;
    }

    public int FindMember(short memberId)
    {
        for (int i = 0; i < MaxMembers; i++)
            if (MemberIds[i] == memberId) return i;
        return -1;
    }
}

public class PartyManager
{
    private readonly Dictionary<int, PartyGroup> _parties = [];
    private int _nextIndex = 1;

    public PartyGroup? GetParty(int index)
    {
        _parties.TryGetValue(index, out var party);
        return party;
    }

    public PartyGroup CreateParty(short leaderId)
    {
        var party = new PartyGroup(_nextIndex);
        party.MemberIds[0] = leaderId;
        _parties[_nextIndex] = party;
        _nextIndex++;
        return party;
    }

    public void DeleteParty(int index)
    {
        _parties.Remove(index);
    }
}
