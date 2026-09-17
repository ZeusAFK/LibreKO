using Godot;

namespace LibreKO;

public enum GameCursorKind
{
    Arrow,
    Held,
    Attack,
    Repair,
    RepairAlt,
}

public static class GameCursor
{
    private const string Root = "res://assets/ui/cursors/";

    private static readonly Input.CursorShape[] Shapes =
    {
        Input.CursorShape.Arrow,
        Input.CursorShape.PointingHand,
    };

    private static readonly System.Collections.Generic.Dictionary<string, Texture2D?> Cache = new();

    private static int _nation = Nations.NotSelected;
    private static GameCursorKind _kind = GameCursorKind.Arrow;
    private static string? _applied;
    private static bool _enabled;

    public static void Enable()
    {
        _enabled = true;
        Apply(force: true);
    }

    public static void Disable()
    {
        _enabled = false;
        _applied = null;
        foreach (var shape in Shapes)
            Input.SetCustomMouseCursor(null, shape);
    }

    public static void SetNation(int nation)
    {
        if (_nation == nation) return;
        _nation = nation;
        Apply();
    }

    public static void Set(GameCursorKind kind)
    {
        if (_kind == kind) return;
        _kind = kind;
        Apply();
    }

    public static void Refresh() => Apply(force: true);

    private static string Stem(GameCursorKind kind) => kind switch
    {
        GameCursorKind.Attack => "attack",
        GameCursorKind.Repair => "repair",
        GameCursorKind.RepairAlt => "repair_alt",
        GameCursorKind.Held => _nation == Nations.ElMorad ? "el_held" : "ka_held",
        _ => _nation == Nations.ElMorad ? "el_idle" : "ka_idle",
    };

    private static void Apply(bool force = false)
    {
        if (!_enabled) return;
        string stem = Stem(_kind);
        if (!force && stem == _applied) return;
        if (Load(stem) is not { } tex) return;
        foreach (var shape in Shapes)
            Input.SetCustomMouseCursor(tex, shape, Vector2.Zero);
        _applied = stem;
    }

    internal static readonly string[] Stems =
        { "attack", "el_idle", "el_held", "ka_idle", "ka_held", "repair", "repair_alt" };

    internal static Texture2D? Peek(string stem) => Load(stem);

    private static Texture2D? Load(string stem)
    {
        if (Cache.TryGetValue(stem, out Texture2D? cached)) return cached;
        string path = $"{Root}{stem}.png";
        Texture2D? tex = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : LoadRaw(path);
        if (tex == null) GD.PushWarning($"[cursor] missing cursor art: {path} (run tools/bake_cursors.py)");
        Cache[stem] = tex;
        return tex;
    }

    private static Texture2D? LoadRaw(string path)
    {
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null) return null;
        var img = new Image();
        return img.LoadPngFromBuffer(file.GetBuffer((long)file.GetLength())) == Error.Ok
            ? ImageTexture.CreateFromImage(img)
            : null;
    }
}
