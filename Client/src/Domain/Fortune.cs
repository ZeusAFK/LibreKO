using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LibreKO.Domain;

public sealed record FortuneReading(int Id, int Category, int Level, string Title, string[] Lines, int Weight);

public sealed record FortuneDeck(Dictionary<string, string> Cards, Dictionary<string, string[]> Frames, FortuneReading[] Readings)
{
    public static readonly FortuneDeck Empty = new(new(), new(), Array.Empty<FortuneReading>());

    public string? CardFor(int category) => Cards.TryGetValue(category.ToString(), out var card) ? card : null;

    public string[] FramesOf(string set) => Frames.TryGetValue(set, out var frames) ? frames : Array.Empty<string>();
}

public static class Fortune
{
    public const int CardsShown = 14;
    public const int EffectSkillBase = 491038;
    public const int MaxStars = 5;
    public const string SpinFrames = "spin";
    public const string FinalFrames = "final";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static FortuneDeck Parse(string json) =>
        JsonSerializer.Deserialize<FortuneDeck>(json, Options) ?? FortuneDeck.Empty;

    public static FortuneReading? Draw(IReadOnlyList<FortuneReading> readings, Func<int, int> roll)
    {
        int total = 0;
        foreach (var reading in readings) total += Math.Max(0, reading.Weight);
        if (total <= 0) return null;

        int pick = roll(total);
        foreach (var reading in readings)
        {
            pick -= Math.Max(0, reading.Weight);
            if (pick < 0) return reading;
        }
        return readings[^1];
    }

    public static int EffectSkill(int category) => EffectSkillBase + category;
}
