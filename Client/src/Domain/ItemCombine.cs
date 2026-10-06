using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LibreKO.Domain;

public sealed record CombineMaterial(int Item, int Count);

public sealed record CombineRecipe(
    int Id, int Npc, int Category, string Name, bool Listed, CombineMaterial[] Materials, int Result,
    int SuccessFx, int SuccessMotion, string SuccessText, int FailureFx, int FailureMotion, string FailureText);

public sealed record CombineCategory(int Key, string Name, int Visibility);

public sealed record CombineBook(CombineCategory[] Categories, CombineRecipe[] Recipes)
{
    public static readonly CombineBook Empty = new(Array.Empty<CombineCategory>(), Array.Empty<CombineRecipe>());

    public CombineRecipe? Row(int id) => Recipes.FirstOrDefault(r => r.Id == id);

    public IReadOnlyList<CombineCategory> Shown => Categories.Where(c => c.Visibility == ItemCombine.ShownCategory).ToList();

    public IReadOnlyList<CombineRecipe> Listed(int category) =>
        Recipes.Where(r => r.Category == category && r.Listed).OrderBy(r => r.Id).ToList();
}

public readonly record struct CombineEntry(int ItemId, int Count, int BagSlot);

public readonly record struct CombineReply(byte Result, short Row, byte BagSlot);

public readonly record struct CombineEffect(bool Success, int NpcId, int Row);

public static class ItemCombine
{
    public const int ShadowPiece = 700009000;
    public const int NoCraftsman = -1;
    public const int MaterialSlots = 10;
    public const int MaxCount = 999;
    public const int ShownCategory = 1;
    public const double Cooldown = 5.0;
    public const double CooldownReady = 1.0;

    public const byte Succeeded = 1;
    public const byte Failed = 2;

    public const int PutMaterialText = 16100;
    public const int WrongMaterialText = 16101;
    public const int ReceivedText = 16102;
    public const int ClassesText = 16104;
    public const int ItemListText = 16105;
    public const int IntroText = 16106;
    public const int UncraftableText = 6313;
    public const int CooldownText = 30702;
    public const int NoBagSlotText = 11409;
    public const int QuantityText = 6034;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static CombineBook Parse(string json) =>
        JsonSerializer.Deserialize<CombineBook>(json, Options) ?? CombineBook.Empty;

    public static IReadOnlyList<CombineEntry> WireOrder(IEnumerable<CombineEntry> entries) =>
        entries.Select((e, i) => (e, i)).OrderBy(x => x.e.ItemId).ThenBy(x => x.i).Select(x => x.e).ToList();

    public static string MaterialText(IEnumerable<CombineEntry> ordered) =>
        string.Concat(ordered.Select(e => $"{e.ItemId:D9}{Math.Clamp(e.Count, 0, MaxCount):D3}"));

    public static int Craftsman(IEnumerable<(int Id, int Template, float Distance)> nearby, int template) =>
        nearby.Where(n => n.Template == template).OrderBy(n => n.Distance).Select(n => n.Id).DefaultIfEmpty(NoCraftsman).First();

    public static bool IsDone(byte result) => result is Succeeded or Failed;

    public static bool Craftable(CombineRecipe recipe, Func<int, bool> known) => recipe.Materials.All(m => known(m.Item));

    public static string Paragraphs(string text) =>
        string.Join("\n\n", text.Replace("\r", "").Split("\n\n")
            .Select(p => string.Join(" ", p.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))));

    public static int SecondsLeft(double remaining) => Math.Max(1, (int)(remaining - CooldownReady + 0.999));
}
