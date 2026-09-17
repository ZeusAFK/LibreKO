using System.Globalization;

namespace LibreKO.Common.Infrastructure.Logging;

public static class LogDirectoryRetention
{
    private const string DirectoryTimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    public static void CleanupOldLogDirectories(string baseDir, int retentionDays)
    {
        if (retentionDays <= 0)
            return;

        var logsRoot = Path.Combine(baseDir, "Logs");
        if (!Directory.Exists(logsRoot))
            return;

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var directory in Directory.EnumerateDirectories(logsRoot))
        {
            var directoryName = Path.GetFileName(directory);
            if (!DateTime.TryParseExact(
                    directoryName,
                    DirectoryTimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var timestamp))
            {
                continue;
            }

            if (timestamp >= cutoff)
                continue;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
