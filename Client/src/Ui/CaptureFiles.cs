using Godot;

namespace LibreKO;

internal static class CaptureFiles
{
    private const string ProjectDir = "res://_capture/";
    private const string UserDir = "user://_capture/";

    internal static bool Exported => OS.HasFeature("template");

    internal static string Path(string fileName)
    {
        string dir = Exported ? UserDir : ProjectDir;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        return ProjectSettings.GlobalizePath(dir + fileName);
    }
}
