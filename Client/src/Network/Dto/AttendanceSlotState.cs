namespace LibreKO.Network;

public static class AttendanceSlotState
{
    public const byte Claimed = 1;
    public const byte Expired = 2;
    public const byte Claimable = 3;
    public const byte ClaimableExtra = 4;
}
