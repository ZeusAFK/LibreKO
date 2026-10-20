using System.Collections.Concurrent;

namespace LibreKO.Game.World;

public class PartyGroup
{
    public const int MaxMembers = 8;

    public int Index { get; }
    public short[] MemberIds { get; } = new short[MaxMembers];

    public short TargetNumberId { get; set; } = -1;

    public short CommandLeaderId { get; set; } = -1;

    public byte BoardType { get; set; }

    private readonly Lock sync = new();

    public bool IsDisbanded { get; private set; }

    public bool IsCommandLeader(short charId)
        => charId == CommanderId;

    public short CommanderId => CommandLeaderId > 0 ? CommandLeaderId : LeaderId;

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

    // A player joins once, into a free slot of a party that still stands.
    public bool TryJoin(short memberId)
    {
        lock (sync)
        {
            var slot = FindEmptySlot();
            if (IsDisbanded || slot < 0 || FindMember(memberId) >= 0)
                return false;

            MemberIds[slot] = memberId;
            return true;
        }
    }

    public bool TryLeave(short memberId)
    {
        lock (sync)
        {
            var slot = FindMember(memberId);
            if (slot < 0)
                return false;

            MemberIds[slot] = -1;
            return true;
        }
    }

    public bool TryPromote(short leaderId, short newLeaderId)
    {
        lock (sync)
        {
            var slot = FindMember(newLeaderId);
            if (IsDisbanded || LeaderId != leaderId || slot <= 0)
                return false;

            (MemberIds[0], MemberIds[slot]) = (MemberIds[slot], MemberIds[0]);
            return true;
        }
    }

    internal short[] Disband()
    {
        lock (sync)
        {
            IsDisbanded = true;
            return [.. MemberIds.Where(memberId => memberId >= 0)];
        }
    }
}

public class PartyManager
{
    private readonly ConcurrentDictionary<int, PartyGroup> parties = new();
    private int lastIndex;

    public PartyGroup? GetParty(int index) => parties.TryGetValue(index, out var party) ? party : null;

    public PartyGroup CreateParty(short leaderId)
    {
        var party = new PartyGroup(Interlocked.Increment(ref lastIndex));
        party.MemberIds[0] = leaderId;
        parties[party.Index] = party;
        return party;
    }

    // The members at the moment the party came down, or null when it was already gone; no one joins after.
    public short[]? DeleteParty(int index) => parties.TryRemove(index, out var party) ? party.Disband() : null;
}
