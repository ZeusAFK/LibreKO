using System;

namespace LibreKO.Domain;

public static class SeekingParty
{
    public const int BandText = 6123;
    public const int SeekingText = 8402;
    private const int LevelGap = 8;
    private const double LevelFactor = 1.5;
    private const int BottomLevel = 1;

    public static (int Min, int Max) Band(int level)
    {
        int low = Math.Min(level - LevelGap, (int)(level / LevelFactor));
        if (low < 0) low = BottomLevel;
        int high = Math.Min(CharacterSheet.MaxLevel, Math.Max(level + LevelGap, (int)(level * LevelFactor)));
        return (low, high);
    }

    public static string? Line(SeekingPartyState state, int level, Func<int, string, string> text) => state switch
    {
        SeekingPartyState.SeekingBand => TextTemplate.Fill(text(BandText, "Seeking Party : Level %d ~ %d"), Band(level).Min, Band(level).Max),
        SeekingPartyState.Seeking => text(SeekingText, "Seeking_Party"),
        _ => null,
    };
}
