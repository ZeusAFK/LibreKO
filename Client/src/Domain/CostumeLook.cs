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

    public static HashSet<int> Dress(int[] gear, bool helmetHidden, Func<int, int> resourceOf,
                                     Func<int, int, bool> graft)
    {
        var claimed = new HashSet<int>();
        int outfit = ResourceIn(gear, InventoryConstants.VisCosPauldron, resourceOf);
        if (outfit != 0 && TypeOf(outfit) == UpperType && graft(UpperPart, outfit))
        {
            claimed.Add(UpperPart);
            foreach (var (type, part) in OutfitPieces)
                if (graft(part, WithType(outfit, type)))
                    claimed.Add(part);
        }

        int helm = helmetHidden ? 0 : ResourceIn(gear, InventoryConstants.VisCosHelmet, resourceOf);
        if (helm != 0 && TypeOf(helm) == HelmType && graft(HeadPart, helm))
            claimed.Add(HeadPart);
        return claimed;
    }

    private static int ResourceIn(int[] gear, int visualSlot, Func<int, int> resourceOf) =>
        visualSlot < gear.Length && gear[visualSlot] > 0 ? resourceOf(gear[visualSlot]) : 0;
}
