namespace LibreKO.Common.Enums;

public enum CharacterRace : byte
{
    KarusArchTuarek = 1,
    KarusTuarek = 2,
    KarusWrinkleTuarek = 3,
    KarusPuriTuarek = 4,
    KarusKurian = 6,
    ElMoradBarbarian = 11,
    ElMoradMale = 12,
    ElMoradFemale = 13,
    ElMoradPorutu = 14,
}

public static class CharacterRaceNations
{
    public static bool BelongsTo(byte race, AccountNation nation) =>
        (CharacterRace)race switch
        {
            CharacterRace.KarusArchTuarek or CharacterRace.KarusTuarek
                or CharacterRace.KarusWrinkleTuarek or CharacterRace.KarusPuriTuarek
                or CharacterRace.KarusKurian => nation == AccountNation.Karus,
            CharacterRace.ElMoradBarbarian or CharacterRace.ElMoradMale
                or CharacterRace.ElMoradFemale
                or CharacterRace.ElMoradPorutu => nation == AccountNation.ElMorad,
            _ => false,
        };
}
