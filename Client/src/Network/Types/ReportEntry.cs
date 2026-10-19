namespace LibreKO.Network;

public readonly struct ReportEntry
{
    public readonly int Id;
    public readonly string TargetName;
    public readonly byte VoteYes;
    public readonly byte VoteNo;
    public readonly string Reason;
    public ReportEntry(int id, string target, byte yes, byte no, string reason)
    { Id = id; TargetName = target; VoteYes = yes; VoteNo = no; Reason = reason; }
}
