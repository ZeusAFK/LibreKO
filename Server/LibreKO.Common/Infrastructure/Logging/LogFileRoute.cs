namespace LibreKO.Common.Infrastructure.Logging;

public sealed class LogFileRoute
{
    public LogFileRoute(string fileName, params string[] sourcePrefixes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        FileName = fileName;
        SourcePrefixes = [..sourcePrefixes
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    public string FileName { get; }

    public IReadOnlyList<string> SourcePrefixes { get; }
}
