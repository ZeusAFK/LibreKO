using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public static class ItemExpiry
{
    public static IReadOnlyList<int> Sweep(ItemSlot[] slots, long nowUnixSeconds, int start = 0, int count = int.MaxValue)
    {
        List<int>? cleared = null;
        var end = (int)Math.Min((long)start + count, slots.Length);
        for (var index = start; index < end; index++)
        {
            if (!slots[index].HasExpired(nowUnixSeconds))
                continue;
            slots[index].Clear();
            (cleared ??= []).Add(index);
        }
        return cleared ?? (IReadOnlyList<int>)[];
    }
}
