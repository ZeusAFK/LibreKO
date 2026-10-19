using System.Collections.Generic;
using System.Text.Json;

namespace LibreKO.Domain;

public static class NpcPlugSlots
{
    public const int Unlimited = int.MaxValue;
    private const int RightHandPlug = 0;
    private const int LeftHandPlug = 1;
    private const string TablePath = "res://assets/npcs/plug_slots.json";

    private static readonly Dictionary<int, int> ByModel = new();
    private static bool loaded;

    public static int For(int modelId)
    {
        EnsureLoaded();
        return ByModel.TryGetValue(modelId, out int slots) ? slots : Unlimited;
    }

    public static int PlugIndex(bool rightHand) => rightHand ? RightHandPlug : LeftHandPlug;

    public static bool Holds(int slots, bool rightHand) => PlugIndex(rightHand) < slots;

    public static void Seed(string json)
    {
        ByModel.Clear();
        using var doc = JsonDocument.Parse(json);
        foreach (var entry in doc.RootElement.EnumerateObject())
            if (int.TryParse(entry.Name, out int modelId) && entry.Value.TryGetInt32(out int slots))
                ByModel[modelId] = slots;
        loaded = true;
    }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        if (GameFiles.ReadText(TablePath) is { } table) Seed(table);
    }
}
