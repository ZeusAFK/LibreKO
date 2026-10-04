using LibreKO.Common.Domain.Entities.GameData;

namespace LibreKO.Game.World;

public readonly record struct PetMaterial(int ItemId, byte BagSlot);

public static class PetTransforms
{
    private const int ItemBaseIdStep = 1000;

    public static int MaterialOf(int itemId) => itemId / ItemBaseIdStep * ItemBaseIdStep;

    public static PetTransformData? Pick(IReadOnlyList<PetTransformData> candidates, Random random)
    {
        var total = candidates.Sum(candidate => Math.Max(0, (int)candidate.Weight));
        if (total <= 0)
            return null;

        var roll = random.Next(total);
        foreach (var candidate in candidates)
        {
            roll -= Math.Max(0, (int)candidate.Weight);
            if (roll < 0)
                return candidate;
        }

        return null;
    }
}
