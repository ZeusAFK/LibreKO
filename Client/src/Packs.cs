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

    public static readonly (string File, string Root)[] Content =
    {
        ("terrain.pck", "res://assets/terrain"),
        ("characters.pck", "res://assets/characters"),
        ("armor.pck", "res://assets/items/armor"),
        ("weapons.pck", "res://assets/items/weapon"),
        ("npcs.pck", "res://assets/npcs"),
        ("objects.pck", "res://assets/objects"),
    };

    public static IReadOnlyList<string> Missing => missing;

    private static readonly List<string> missing = new();
    private static bool downloadedMounted;

    public override void _Ready()
    {
        Diag.Install();
        RenderFaultWatch.Install();
        if (OS.HasFeature("editor")) return;
        Config.PinShippedDefaults();
        Build.Pin();
        if (Platform.BundledContent)
        {
            ContentReady = PacksIn(DownloadedContentDir).Count > 0;
            if (!ContentReady) GD.Print("[packs] no downloaded content — first run must fetch it");
            return;
        }

        string dir = OS.GetExecutablePath().GetBaseDir().PathJoin(ContentDir);
        foreach (var (pck, root) in Content)
            Mount(dir.PathJoin(pck), required: !DirAccess.DirExistsAbsolute(root));

        MountLooseDir(dir.PathJoin("patches"), "patch");

        if (missing.Count > 0)
            GD.PushError($"[packs] INCOMPLETE INSTALL — missing or unreadable: {string.Join(", ", missing)}. " +
                         "Content from these packs will fail to load.");
    }

    public static void MountDownloaded()
    {
        if (downloadedMounted) return;
        downloadedMounted = true;
        int content = MountLooseDir(DownloadedContentDir, "content");
        MountLooseDir(DownloadedPatchDir, "patch");
        ContentReady = content > 0;
    }

    private static List<string> PacksIn(string dir)
    {
        var names = new List<string>();
        using var d = DirAccess.Open(dir);
        if (d == null) return names;
        d.ListDirBegin();
        for (string f = d.GetNext(); f.Length > 0; f = d.GetNext())
            if (!d.CurrentIsDir() && f.EndsWith(".pck"))
                names.Add(f);
        names.Sort();
        return names;
    }

    private static int MountLooseDir(string dir, string label)
    {
        var names = PacksIn(dir);
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
                missing.Add(name);
            return;
        }
        if (!ProjectSettings.LoadResourcePack(osPath))
        {
            GD.PushError($"[packs] failed to mount {osPath} (corrupt, truncated, or wrong engine version)");
            if (required)
                missing.Add(name);
            return;
        }
        GD.Print($"[packs] mounted {name}");
    }
}
