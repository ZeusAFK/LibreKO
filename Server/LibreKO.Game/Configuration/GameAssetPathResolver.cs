namespace LibreKO.Game.Configuration;

internal static class GameAssetPathResolver
{
    public static string[] GetCandidateDirectories(string configuredDirectory, string contentRootPath, string defaultDirectoryName)
    {
        var directories = new List<string>();

        AddConfiguredDirectory(directories, configuredDirectory, contentRootPath);
        AddDirectory(directories, Path.Combine(contentRootPath, defaultDirectoryName));
        AddDirectory(directories, Path.Combine(AppContext.BaseDirectory, defaultDirectoryName));

        return [..directories];
    }

    public static string? ResolveExistingDirectory(string configuredDirectory, string contentRootPath, string defaultDirectoryName)
    {
        return GetCandidateDirectories(configuredDirectory, contentRootPath, defaultDirectoryName)
            .FirstOrDefault(Directory.Exists);
    }

    private static void AddConfiguredDirectory(List<string> directories, string configuredDirectory, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
            return;

        if (Path.IsPathRooted(configuredDirectory))
        {
            AddDirectory(directories, configuredDirectory);
            return;
        }

        AddDirectory(directories, Path.GetFullPath(Path.Combine(contentRootPath, configuredDirectory)));
        AddDirectory(directories, Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configuredDirectory)));
    }

    private static void AddDirectory(List<string> directories, string path)
    {
        if (directories.Contains(path, StringComparer.OrdinalIgnoreCase))
            return;

        directories.Add(path);
    }
}
