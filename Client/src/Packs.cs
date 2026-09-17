using Godot;
using System.Collections.Generic;
using FileAccess = Godot.FileAccess;

namespace LibreKO;

public partial class Packs : Node
{
    public const string ContentDir = "content";
    public const string DownloadedContentDir = "user://content";
    public const string DownloadedPatchDir = "user://patches";

    public static bool ContentReady { get; private set; }

    public static readonly string[] Content =
    {
        "terrain.pck", "characters.pck", "armor.pck", "weapons.pck", "npcs.pck", "objects.pck",
    };

    public static IReadOnlyList<string> Missing => _missing;

    private static readonly List<string> _missing = new();

    public override void _Ready()
    {
        Diag.Install();
        if (OS.HasFeature("editor")) return;
        if (Platform.BundledContent) { MountDownloaded(); return; }

        string dir = OS.GetExecutablePath().GetBaseDir().PathJoin(ContentDir);
        foreach (var pck in Content)
            Mount(dir.PathJoin(pck), required: true);

        MountLooseDir(dir.PathJoin("patches"), "patch");

        if (_missing.Count > 0)
            GD.PushError($"[packs] INCOMPLETE INSTALL — missing or unreadable: {string.Join(", ", _missing)}. " +
                         "Content from these packs will fail to load.");
    }

    public static void MountDownloaded()
    {
        int content = MountLooseDir(DownloadedContentDir, "content");
        MountLooseDir(DownloadedPatchDir, "patch");
        ContentReady = content > 0;
        if (!ContentReady)
            GD.Print("[packs] no downloaded content — first run must fetch it");
    }

    private static int MountLooseDir(string dir, string label)
    {
        using var d = DirAccess.Open(dir);
        if (d == null) return 0;
        var names = new List<string>();
        d.ListDirBegin();
        for (string f = d.GetNext(); f.Length > 0; f = d.GetNext())
            if (!d.CurrentIsDir() && f.EndsWith(".pck"))
                names.Add(f);
        names.Sort();
        foreach (var n in names)
            Mount(dir.PathJoin(n), required: false);
        if (names.Count > 0)
            GD.Print($"[packs] {names.Count} {label} pack(s): {string.Join(", ", names)}");
        return names.Count;
    }

    private static void Mount(string osPath, bool required)
    {
        string name = osPath.GetFile();
        if (!FileAccess.FileExists(osPath))
        {
            if (required)
                _missing.Add(name);
            return;
        }
        if (!ProjectSettings.LoadResourcePack(osPath))
        {
            GD.PushError($"[packs] failed to mount {osPath} (corrupt, truncated, or wrong engine version)");
            if (required)
                _missing.Add(name);
            return;
        }
        GD.Print($"[packs] mounted {name}");
    }
}
