using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LibreKO.Domain;

public sealed record DisguiseForm(int Id, string Name, int Level, int Skill, int Item, int Access, string Note, int ClassLimit);

public sealed record DisguiseGroup(int Level, IReadOnlyList<DisguiseForm> Forms);

public enum DisguiseRefusal
{
    None,
    Level,
    Transformed,
}

public static class Disguise
{
    public const int Everyone = 1;
    public const int WithoutPremium = 2;
    public const int PremiumOnly = 3;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static DisguiseForm[] Parse(string json) =>
        JsonSerializer.Deserialize<DisguiseForm[]>(json, Options) ?? Array.Empty<DisguiseForm>();

    public static IReadOnlyList<DisguiseForm> FormsFor(IEnumerable<DisguiseForm> all, int listItem, int premiumType) =>
        all.Where(f => f.Item == listItem && Allows(f.Access, premiumType)).ToList();

    public static bool Allows(int access, int premiumType) => access switch
    {
        WithoutPremium => premiumType == 0,
        PremiumOnly => premiumType != 0,
        _ => true,
    };

    public static IReadOnlyList<DisguiseGroup> Groups(IEnumerable<DisguiseForm> forms) =>
        forms.GroupBy(f => f.Level)
            .OrderBy(g => g.Key)
            .Select(g => new DisguiseGroup(g.Key, g.ToList()))
            .ToList();

    public static DisguiseRefusal Refusal(DisguiseForm form, int level, bool transformed) =>
        level < form.Level ? DisguiseRefusal.Level
        : transformed ? DisguiseRefusal.Transformed
        : DisguiseRefusal.None;
}
