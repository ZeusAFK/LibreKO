using LibreKO.Common.Domain.Services;

namespace LibreKO.Game.World;

public enum DropBand : byte
{
    Common = 1,
    Uncommon = 2,
    Rare = 3,
    VeryRare = 4,
}

public readonly record struct DropLine(int ItemId, DropBand Band);

public sealed record DropSummary(int CoinMin, int CoinMax, int Experience, IReadOnlyList<DropLine> Items, int Total);

public static class DropSummaries
{
    public const int MaxLines = 64;
    private const int PercentScale = 10000;
    private const int CommonFrom = 5000;
    private const int UncommonFrom = 1000;
    private const int RareFrom = 100;
    private const int CoinMinPercent = 70;
    private const int ItemIdFloor = 100_000_000;
    private const int GroupIdFloor = 100;

    public static DropBand BandOf(int chance) => chance switch
    {
        >= CommonFrom => DropBand.Common,
        >= UncommonFrom => DropBand.Uncommon,
        >= RareFrom => DropBand.Rare,
        _ => DropBand.VeryRare,
    };

    public static DropSummary For(NpcInstance npc, IGameDataService gameData)
    {
        var lines = new List<DropLine>();
        var seen = new HashSet<int>();
        var table = npc.DropItemGroup > 0 ? gameData.GetNpcItem(npc.DropItemGroup, npc.IsMonster) : null;
        if (table != null)
        {
            foreach (var (dropValue, percent) in table.GetDrops())
            {
                if (dropValue <= 0 || percent <= 0)
                    continue;

                if (dropValue >= ItemIdFloor)
                {
                    Add(lines, seen, gameData, dropValue, percent);
                    continue;
                }

                if (dropValue >= GroupIdFloor && gameData.MakeItemGroupTable.TryGetValue(dropValue, out var pool)
                    && pool.Length > 0)
                    foreach (var itemId in pool)
                        Add(lines, seen, gameData, itemId, Math.Max(1, percent / pool.Length));
            }
        }

        lines.Sort((a, b) => a.Band != b.Band ? a.Band.CompareTo(b.Band) : a.ItemId.CompareTo(b.ItemId));
        int coinMax = Math.Max(0, npc.GoldDrop);
        return new DropSummary(coinMax * CoinMinPercent / 100, coinMax, Math.Max(0, npc.Experience),
            lines.Take(MaxLines).ToList(), lines.Count);
    }

    private static void Add(List<DropLine> lines, HashSet<int> seen, IGameDataService gameData, int itemId, int chance)
    {
        if (chance > PercentScale) chance = PercentScale;
        if (gameData.GetItem(itemId) == null || !seen.Add(itemId))
            return;
        lines.Add(new DropLine(itemId, BandOf(chance)));
    }
}
