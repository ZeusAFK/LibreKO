using Godot;
using FileAccess = Godot.FileAccess;

namespace LibreKO;

public static class Build
{
    private const string VersionResPath = "res://version.txt";
    private const string ApkBuildResPath = "res://apkbuild.txt";

    private static string? _version;
    private static int _apkBuild = -1;

    public static string Version => _version ??= ReadVersion();

    public static int ApkBuild => _apkBuild >= 0 ? _apkBuild : _apkBuild = ReadApkBuild();

    private static int ReadApkBuild()
    {
        using var f = FileAccess.Open(ApkBuildResPath, FileAccess.ModeFlags.Read);
        return f != null && int.TryParse(f.GetAsText().Trim(), out int n) ? n : 0;
    }

    private static string ReadVersion()
    {
        using var f = FileAccess.Open(VersionResPath, FileAccess.ModeFlags.Read);
        if (f == null)
            return "dev";
        string v = f.GetAsText().Trim();
        return v.Length > 0 ? v : "dev";
    }
}
