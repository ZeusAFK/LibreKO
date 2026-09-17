using LibreKO.Common.Enums;

namespace LibreKO.Common.Domain.Entities.GameData;

public class ItemSlot
{
    public const int SecondsPerHour = 3_600;
    public const int HoursPerDay = 24;
    public const int SecondsPerDay = SecondsPerHour * HoursPerDay;

    public int ItemId { get; set; }
    public short Durability { get; set; }
    public ushort Count { get; set; }
    public byte Flag { get; set; }
    public long ExpiresAt { get; set; }

    public ItemFlag State => (ItemFlag)Flag;

    public bool IsEmpty => ItemId == 0;

    public bool IsTradable => State
        is not (ItemFlag.Rented or ItemFlag.CharacterSeal or ItemFlag.Duplicate
            or ItemFlag.Sealed or ItemFlag.Bound);

    public bool Expires => ExpiresAt != 0;

    public bool HasExpired(long nowUnixSeconds) => ExpiresAt != 0 && ExpiresAt <= nowUnixSeconds;

    public void ExpireInDays(int days, long nowUnixSeconds) =>
        ExpireInHours(days * HoursPerDay, nowUnixSeconds);

    public void ExpireInHours(int hours, long nowUnixSeconds)
    {
        if (hours <= 0)
            return;
        ExpiresAt = nowUnixSeconds + (long)hours * SecondsPerHour;
        Flag = (byte)ItemFlag.Rented;
    }

    public void Clear()
    {
        ItemId = 0;
        Durability = 0;
        Count = 0;
        Flag = 0;
        ExpiresAt = 0;
    }
}
