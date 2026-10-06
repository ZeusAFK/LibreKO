using System.Collections.Generic;

namespace LibreKO.Domain;

public static class PlayerRig
{
    public const int KarusBase = 1;
    public const int KarusKurian = 6;
    public const int ElMoradBase = 11;
    public const int ElMoradKurian = 14;

    public static bool IsKurian(int race) => race is KarusKurian or ElMoradKurian;

    public static int StandardRace(int race) => race switch
    {
        KarusKurian => KarusBase,
        ElMoradKurian => ElMoradBase,
        _ => race,
    };

    public static bool SameBones(IReadOnlyList<string> own, IReadOnlyList<string> worn)
    {
        if (own.Count == 0 || own.Count != worn.Count) return false;
        for (int i = 0; i < own.Count; i++)
            if (own[i] != worn[i]) return false;
        return true;
    }
}
