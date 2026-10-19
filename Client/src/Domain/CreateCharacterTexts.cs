namespace LibreKO.Domain;

public static class CreateCharacterTexts
{
    public const int TitleText = 1901;
    public const int UnknownText = 1915;

    public static int TextFor(CreateCharacterResult code) => code switch
    {
        CreateCharacterResult.SlotFull => 1911,
        CreateCharacterResult.NationRaceMismatch => 1907,
        CreateCharacterResult.NameAlreadyExists => 1912,
        CreateCharacterResult.ServerError => 1902,
        CreateCharacterResult.InvalidName => 1906,
        CreateCharacterResult.NameMissing => 1905,
        CreateCharacterResult.InvalidRace => 1908,
        CreateCharacterResult.UnsupportedRace => 1910,
        CreateCharacterResult.InvalidClass => 1904,
        CreateCharacterResult.PointsRemaining => 1913,
        CreateCharacterResult.InvalidCharacters => 7654,
        _ => UnknownText,
    };
}
