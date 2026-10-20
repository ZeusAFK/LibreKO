using System.IO;
using System.Runtime.CompilerServices;

namespace LibreKO.Tests;

internal static class ClientFiles
{
    private const string ProjectFile = "project.godot";

    public static string Dir([CallerFilePath] string source = "")
    {
        string root = Path.Combine(Path.GetDirectoryName(source)!, "..", "..");
        string nested = Path.Combine(root, "client");
        return File.Exists(Path.Combine(nested, ProjectFile)) ? nested : root;
    }

    public static string Read(string relative) =>
        File.ReadAllText(Path.Combine(Dir(), relative)).Replace("\r\n", "\n");
}
