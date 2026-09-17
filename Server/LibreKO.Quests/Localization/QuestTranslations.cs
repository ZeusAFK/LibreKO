using System.Collections.Concurrent;

namespace LibreKO.Quests.Localization;

public interface IQuestTranslations
{
    string Translate(string languageCode, string text);

    bool Knows(string languageCode);
}

public sealed class QuestTranslations : IQuestTranslations
{
    public const string SourceLanguage = "en";
    public const string FileExtension = ".po";

    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _byLanguage =
        new(StringComparer.OrdinalIgnoreCase);

    public static QuestTranslations Empty { get; } = new();

    public IReadOnlyCollection<string> Languages => (IReadOnlyCollection<string>)_byLanguage.Keys;

    public bool Knows(string languageCode) => _byLanguage.ContainsKey(languageCode);

    public string Translate(string languageCode, string text)
    {
        if (text.Length == 0 || string.Equals(languageCode, SourceLanguage, StringComparison.OrdinalIgnoreCase))
            return text;

        if (!_byLanguage.TryGetValue(languageCode, out var table))
            return text;

        return table.TryGetValue(text, out var translated) && translated.Length > 0 ? translated : text;
    }

    public void Load(string languageCode, IEnumerable<string> poLines)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in PoFile.Parse(poLines))
        {
            if (entry.MsgId.Length == 0 || entry.MsgStr.Length == 0)
                continue;
            table[entry.MsgId] = entry.MsgStr;
        }
        _byLanguage[languageCode] = table;
    }

    public int LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;

        var loaded = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*" + FileExtension))
        {
            var code = Path.GetFileNameWithoutExtension(path);
            if (code.Length == 0)
                continue;
            Load(code, File.ReadLines(path));
            loaded++;
        }
        return loaded;
    }

    public int Count(string languageCode) =>
        _byLanguage.TryGetValue(languageCode, out var table) ? table.Count : 0;
}
