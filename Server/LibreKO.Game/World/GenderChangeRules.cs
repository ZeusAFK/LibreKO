using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class GenderChangeRules
{
    public static IReadOnlyList<byte> AllowedRaces(short classId)
    {
        var karus = ClassIdHelper.GetNation(classId) == AccountNation.Karus;
        CharacterRace[] races =
            ClassIdHelper.IsPortuKurian(classId) ? [karus ? CharacterRace.KarusKurian : CharacterRace.ElMoradPorutu]
            : ClassIdHelper.IsWarrior(classId) ? karus
                ? [CharacterRace.KarusArchTuarek]
                : [CharacterRace.ElMoradBarbarian, CharacterRace.ElMoradMale, CharacterRace.ElMoradFemale]
            : ClassIdHelper.IsRogue(classId) ? karus
                ? [CharacterRace.KarusTuarek]
                : [CharacterRace.ElMoradMale, CharacterRace.ElMoradFemale]
            : ClassIdHelper.IsMage(classId) ? karus
                ? [CharacterRace.KarusWrinkleTuarek, CharacterRace.KarusPuriTuarek]
                : [CharacterRace.ElMoradMale, CharacterRace.ElMoradFemale]
            : ClassIdHelper.IsPriest(classId) ? karus
                ? [CharacterRace.KarusTuarek, CharacterRace.KarusPuriTuarek]
                : [CharacterRace.ElMoradMale, CharacterRace.ElMoradFemale]
            : [];
        return races.Select(race => (byte)race).ToArray();
    }

    public static bool CanChange(short classId) => AllowedRaces(classId).Count > 1;

    public static bool Allows(short classId, byte race) => CanChange(classId) && AllowedRaces(classId).Contains(race);
}
