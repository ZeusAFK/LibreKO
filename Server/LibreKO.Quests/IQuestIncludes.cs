namespace LibreKO.Quests;

public interface IQuestIncludes
{
    bool TryRead(string path, out string text, out string fileName);
    IQuestIncludes ForFile(string fileName) => this;
}

public sealed class NoQuestIncludes : IQuestIncludes
{
    public static readonly NoQuestIncludes Instance = new();

    public bool TryRead(string path, out string text, out string fileName)
    {
        text = string.Empty;
        fileName = path;
        return false;
    }
}

public sealed class DirectoryQuestIncludes(
    string root,
    string extension = ".quest",
    int ancestorsSearched = 3) : IQuestIncludes
{
    public IQuestIncludes ForFile(string fileName) =>
        new DirectoryQuestIncludes(Path.GetDirectoryName(fileName) ?? root, extension, ancestorsSearched);

    public bool TryRead(string path, out string text, out string fileName)
    {
        text = string.Empty;
        fileName = path;

        var relative = path.Replace('\\', '/').Trim('/');
        if (relative.Length == 0 || relative.Split('/').Contains(".."))
            return false;

        var directory = new DirectoryInfo(Path.GetFullPath(root));
        for (var level = 0; level <= ancestorsSearched && directory is not null; level++)
        {
            var candidate = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!Path.HasExtension(candidate))
                candidate += extension;

            if (File.Exists(candidate))
            {
                text = File.ReadAllText(candidate);
                fileName = Path.GetFullPath(candidate);
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }
}
