namespace LibreKO.Quests;

public static class QuestUri
{
    public static string? LocalPath(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        // .NET reads the "f%3A" an editor sends as a path segment, not a drive
        var unescaped = Uri.UnescapeDataString(uri);
        if (!Uri.TryCreate(unescaped, UriKind.Absolute, out var parsed))
            return null;
        if (!parsed.IsFile)
            return null;

        var path = parsed.LocalPath;
        return path.Length == 0 ? null : path;
    }

    public static string? DirectoryOf(string uri)
    {
        var path = LocalPath(uri);
        if (path is null or { Length: 0 })
            return null;

        var directory = Path.GetDirectoryName(path);
        return directory is { Length: > 0 } && Directory.Exists(directory) ? directory : null;
    }
}
