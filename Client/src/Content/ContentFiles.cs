namespace LibreKO.Content;

internal static class ContentFiles
{
    internal static string? ReadText(string path) =>
        Godot.FileAccess.FileExists(path) ? Godot.FileAccess.GetFileAsString(path) : null;
}
