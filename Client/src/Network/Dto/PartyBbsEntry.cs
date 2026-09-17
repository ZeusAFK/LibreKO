namespace LibreKO.Network;

public readonly struct PartyBbsEntry
{
    public readonly string Name;
    public readonly int ClassOrWanted;
    public readonly int Level;
    public readonly byte Type;
    public readonly string Message;
    public readonly int ZoneId;
    public readonly int MemberCount;
    public readonly int Nation;

    public PartyBbsEntry(string name, int classOrWanted, int level, byte type,
        string message, int zoneId, int memberCount, int nation)
    {
        Name = name; ClassOrWanted = classOrWanted; Level = level; Type = type;
        Message = message; ZoneId = zoneId; MemberCount = memberCount; Nation = nation;
    }

    public bool IsLeaderRecruiting => Type == 3;
}
