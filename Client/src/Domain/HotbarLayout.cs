using System.Globalization;

namespace LibreKO.Domain;

public static class HotbarLayout
{
    public const int Pages = 8;
    public const int SlotsPerPage = 10;
    public const int Total = Pages * SlotsPerPage;
    public const int MaxBars = 3;
    public const int LegacySlotsPerPage = 8;
    public const int LegacyTotal = Pages * LegacySlotsPerPage;

    public static int[] Normalize(int[] saved)
    {
        var slots = new int[Total];
        int perPage = saved.Length == LegacyTotal ? LegacySlotsPerPage : SlotsPerPage;
        for (int i = 0; i < saved.Length; i++)
        {
            int page = i / perPage;
            if (page >= Pages) break;
            slots[page * SlotsPerPage + i % perPage] = saved[i];
        }
        return slots;
    }

    public static string KeyLabel(int slotInPage) =>
        ((slotInPage + 1) % SlotsPerPage).ToString(CultureInfo.InvariantCulture);
}
