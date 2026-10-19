using System.Collections.Generic;

namespace LibreKO.Domain;

public static class PlayerRig
{
    public const int KarusBase = 1;
    public const int ArkTuarek = 1;
    public const int Tuarek = 2;
    public const int WrinkleTuarek = 3;
    public const int PuriTuarek = 4;
    public const int PuriTuarekMagician = 5;
    public const int KarusKurian = 6;
    public const int ElMoradBase = 11;
    public const int Barbarian = 11;
    public const int ElMoradMan = 12;
    public const int ElMoradWoman = 13;
    public const int ElMoradKurian = 14;

    private const int MageFamily = 3;

    public static bool IsKurian(int race) => race is KarusKurian or ElMoradKurian;

    public static int LookRace(int race, int classCode) =>
        race == PuriTuarek && CharacterClassCatalog.Family(classCode) == MageFamily ? PuriTuarekMagician : race;

    public static int PartRace(int lookRace) => lookRace == PuriTuarekMagician ? PuriTuarek : lookRace;

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
