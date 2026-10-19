using Godot;

namespace LibreKO.Presentation;

internal static class NpcRoleFx
{
    private const string TablePath = "res://assets/npcs/role_fx.json";
    // Same rise for every bundle: keying off each one's own extent instead made the wide-winged
    // icons ride visibly higher than the compact ones.
    private const float IndicatorRise = 1.40f;
    private const float FallbackMinTop = 1.8f;
    private const float FallbackTopGap = 0.30f;

    private static Dictionary<int, string>? roleByNpc;
    private static Dictionary<string, string>? markerByState;

    internal static string ReadyMarker => Marker("readyToTurnIn");

    internal static string InProgressMarker => Marker("inProgress");

    internal static string AvailableMarker => Marker("available");

    internal static bool HasRole(int npcId) => Roles.ContainsKey(npcId);

    internal static Node3D? Spawn(EntityView ent)
    {
        if (!ent.IsNpc || ent.IsMonster || ent.NpcId <= 0) return null;
        return Roles.TryGetValue(ent.NpcId, out string? fx) ? SpawnIndicator(ent, fx) : null;
    }

    internal static void Warm(Node host)
    {
        var names = new HashSet<string>(Roles.Values);
        if (markerByState != null)
            foreach (var fx in markerByState.Values) names.Add(fx);
        var warm = new Node3D { Name = "FxWarm", Visible = false };
        host.AddChild(warm);
        foreach (var name in names) Fx.Spawn(name, warm, Vector3.Zero);
        warm.QueueFree();
    }

    internal static Node3D? SpawnIndicator(EntityView ent, string fxName)
    {
        float top = ent.NameTag?.Position.Y
            ?? Mathf.Max(FallbackMinTop, ModelMetrics.TopY(ent.Body) + FallbackTopGap);
        return Fx.Spawn(
            fxName,
            ent.Body,
            new Vector3(0f, top + IndicatorRise - Fx.AuthoredCentreY(fxName), 0f));
    }

    private static Dictionary<int, string> Roles => roleByNpc ??= Load();

    private static string Marker(string state)
    {
        _ = Roles;
        return markerByState != null && markerByState.TryGetValue(state, out string? fx) ? fx : "";
    }

    private static Dictionary<int, string> Load()
    {
        var result = new Dictionary<int, string>();
        markerByState = new Dictionary<string, string>();
        using var file = Godot.FileAccess.Open(TablePath, Godot.FileAccess.ModeFlags.Read);
        if (file == null) return result;
        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return result;
        var root = parsed.AsGodotDictionary();
        if (root.TryGetValue("questMarkers", out var markers)
            && markers.VariantType == Variant.Type.Dictionary)
            foreach (var pair in markers.AsGodotDictionary())
                markerByState[pair.Key.AsString()] = pair.Value.AsString();
        if (!root.TryGetValue("npcs", out var value)
            || value.VariantType != Variant.Type.Dictionary)
            return result;
        foreach (var pair in value.AsGodotDictionary())
            if (int.TryParse(pair.Key.AsString(), out int npcId))
                result[npcId] = pair.Value.AsString();
        return result;
    }
}
