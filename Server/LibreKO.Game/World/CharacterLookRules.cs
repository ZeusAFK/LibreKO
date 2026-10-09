using LibreKO.Common.Enums;

namespace LibreKO.Game.World;

public static class CharacterLookRules
{
    public const int HairStyleShift = 24;
    private const int HairStyleMask = byte.MaxValue;

    private sealed record LookRange(int Faces, int HairStyles);

    private static readonly Dictionary<CharacterRace, LookRange> Ranges = new()
    {
        [CharacterRace.KarusArchTuarek] = new(Faces: 4, HairStyles: 4),
        [CharacterRace.KarusTuarek] = new(Faces: 4, HairStyles: 4),
        [CharacterRace.KarusWrinkleTuarek] = new(Faces: 8, HairStyles: 4),
        [CharacterRace.KarusPuriTuarek] = new(Faces: 8, HairStyles: 8),
        [CharacterRace.KarusKurian] = new(Faces: 1, HairStyles: 0),
        [CharacterRace.ElMoradBarbarian] = new(Faces: 8, HairStyles: 7),
        [CharacterRace.ElMoradMale] = new(Faces: 8, HairStyles: 7),
        [CharacterRace.ElMoradFemale] = new(Faces: 8, HairStyles: 7),
        [CharacterRace.ElMoradPorutu] = new(Faces: 1, HairStyles: 0),
    };

    public static int StyleOf(int hair) => (hair >> HairStyleShift) & HairStyleMask;

    public static bool Allows(byte race, byte face, int hair, int currentHair) =>
        Ranges.TryGetValue((CharacterRace)race, out var range)
        && face < range.Faces
        && (range.HairStyles == 0
            ? StyleOf(hair) == StyleOf(currentHair)
            : StyleOf(hair) < range.HairStyles);
}
