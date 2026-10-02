using System;
using System.Collections.Generic;

namespace LibreKO.Domain;

public static class CostumeLook
{
    public const int UpperType = 10;
    public const int LowerType = 20;
    public const int HelmType = 30;
    public const int HandsType = 40;
    public const int FeetType = 50;

    public const int UpperPart = 0;
    public const int LowerPart = 1;
    public const int HandsPart = 2;
    public const int FeetPart = 3;
    public const int HeadPart = 5;

    private const int CategorySpan = 10_000_000;
    private const int ModelSpan = 1000;
    private const int ModelDigits = 10_000;
    private const int TypeSpan = 10;
    private const int TypeDigits = 100;
    private const int PrefixSpan = 10_000;
    private const int PrefixDigits = 100;

    private static readonly (int Type, int Part)[] OutfitPieces =
    {
        (LowerType, LowerPart), (HandsType, HandsPart), (FeetType, FeetPart),
    };

    public static int TypeOf(int resource) => resource / TypeSpan % TypeDigits;

    public static int WithType(int resource, int type) =>
        resource / ModelSpan * ModelSpan + type * TypeSpan + resource % TypeSpan;

    public static string ArmorStem(int resource, int race)
    {
        int category = resource / CategorySpan;
        int model = resource / ModelSpan % ModelDigits + race;
        return $"{category}_{model:D4}_{TypeOf(resource):D2}_{resource % TypeSpan}";
    }

    public static int PartPrefix(int itemId, int saleType) =>
        saleType == ItemData.SaleTypeLowNoRepair ? 0 : itemId / PrefixSpan % PrefixDigits;

    public static string[] PartStems(int prefix, int resource, int race)
    {
        string stem = ArmorStem(resource, race);
        return prefix == 0 ? new[] { stem } : new[] { $"{prefix:D2}_{stem}", stem };
    }

    public static HashSet<int> Dress(int[] gear, bool helmetHidden, Func<int, int> resourceOf,
                                     Func<int, int, int, bool> graft)
    {
        var claimed = new HashSet<int>();
        int outfitItem = ItemIn(gear, InventoryConstants.VisCosPauldron);
        int outfit = outfitItem > 0 ? resourceOf(outfitItem) : 0;
        if (outfit != 0 && TypeOf(outfit) == UpperType && graft(UpperPart, outfit, outfitItem))
        {
            claimed.Add(UpperPart);
            foreach (var (type, part) in OutfitPieces)
                if (graft(part, WithType(outfit, type), outfitItem))
                    claimed.Add(part);
        }

        int helmItem = helmetHidden ? 0 : ItemIn(gear, InventoryConstants.VisCosHelmet);
        int helm = helmItem > 0 ? resourceOf(helmItem) : 0;
        if (helm != 0 && TypeOf(helm) == HelmType && graft(HeadPart, helm, helmItem))
            claimed.Add(HeadPart);
        return claimed;
    }

    private static int ItemIn(int[] gear, int visualSlot) =>
        visualSlot < gear.Length && gear[visualSlot] > 0 ? gear[visualSlot] : 0;
}
