using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class NationTransferRules
{
    private const short NationClassStep = 100;

    public static AccountNation OtherNation(AccountNation nation) =>
        nation == AccountNation.Karus ? AccountNation.ElMorad : AccountNation.Karus;

    public static short NewClass(short classId) =>
        ClassIdHelper.GetNation(classId) == AccountNation.Karus
            ? (short)(classId + NationClassStep)
            : (short)(classId - NationClassStep);

    public static bool IsFemale(byte race) =>
        race is (byte)CharacterRace.KarusPuriTuarek or (byte)CharacterRace.ElMoradFemale;

    public static byte ProposedRace(byte race, short classId)
    {
        var allowed = GenderChangeRules.AllowedRaces(NewClass(classId));
        if (allowed.Count == 0)
            return 0;
        if (race == (byte)CharacterRace.KarusArchTuarek && allowed.Contains((byte)CharacterRace.ElMoradBarbarian))
            return (byte)CharacterRace.ElMoradBarbarian;
        return allowed.FirstOrDefault(
            candidate => candidate != (byte)CharacterRace.ElMoradBarbarian && IsFemale(candidate) == IsFemale(race),
            allowed[0]);
    }

    public static bool Allows(short classId, byte race) =>
        GenderChangeRules.AllowedRaces(NewClass(classId)).Contains(race);
}
