namespace LibreKO.Network;

public enum ReportSubOpcode : byte
{
    File = 9,
    VoteYes = 12,
    VoteNo = 13,
    ListOpen = 14,
    Inspector = 18,
}

public static class ReportWire
{
    public const int PageSize = 8;
    public const int ReasonMax = 512;
}
