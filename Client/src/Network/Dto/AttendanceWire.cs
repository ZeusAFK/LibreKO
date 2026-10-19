namespace LibreKO.Network;

public static class AttendanceWire
{
    public const int DailySlots = 25;
    public const int BonusSlots = 3;
    public const int BonusFirstSlot = 101;

    public static int BonusThreshold(int slot) => 15 + (slot - BonusFirstSlot) * 5;
}
