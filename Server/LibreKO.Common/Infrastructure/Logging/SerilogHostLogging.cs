using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace LibreKO.Common.Infrastructure.Logging;

public static class SerilogHostLogging
{
    private const string ConsoleTemplate =
        "[{Level:u3}] {ShortSourceContext}: {Message:lj}{NewLine}{Exception}";

    private const string FileTemplate =
        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static Serilog.ILogger CreateLogger(
        string baseDir,
        IEnumerable<LogFileRoute> routes,
        IEnumerable<string> consoleSourcePrefixes,
        string? fileLevelName = null,
        string? consoleLevelName = null,
        int retentionDays = 10,
        ErrorTrackingOptions? errorTracking = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDir);

        LogDirectoryRetention.CleanupOldLogDirectories(baseDir, retentionDays);

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var logDir = Path.Combine(baseDir, "Logs", timestamp);
        Directory.CreateDirectory(logDir);

        var routeList = routes.ToArray();
        var consolePrefixes = consoleSourcePrefixes
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var routedPrefixes = routeList
            .SelectMany(static route => route.SourcePrefixes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var fileLevel = ParseLevel(fileLevelName, LogEventLevel.Verbose);
        var consoleLevel = ParseLevel(consoleLevelName, LogEventLevel.Warning);

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(fileLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.With<ShortSourceContextEnricher>()
            .WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => ShouldWriteToConsole(e, consolePrefixes, consoleLevel))
                .WriteTo.Sink(new TranceConsoleSink()));

        if (errorTracking is not null)
        {
            loggerConfiguration = loggerConfiguration.WriteTo.Sentry(o =>
            {
                o.Dsn = errorTracking.Dsn;
                o.MinimumBreadcrumbLevel = LogEventLevel.Information;
                o.MinimumEventLevel = LogEventLevel.Error;
                o.Release = errorTracking.Release;
                o.Environment = errorTracking.Environment;
                o.ServerName = errorTracking.ServerName;
                o.AttachStacktrace = true;
            });
        }

        foreach (var route in routeList)
        {
            var sourcePrefixes = route.SourcePrefixes.ToArray();
            loggerConfiguration = loggerConfiguration.WriteTo.Logger(lc => lc
                .Filter.ByIncludingOnly(e => MatchesAnySource(e, sourcePrefixes))
                .WriteTo.File(
                    Path.Combine(logDir, route.FileName),
                    outputTemplate: FileTemplate,
                    flushToDiskInterval: TimeSpan.FromSeconds(1)));
        }

        loggerConfiguration = loggerConfiguration.WriteTo.Logger(lc => lc
            .Filter.ByExcluding(e => MatchesAnySource(e, routedPrefixes))
            .WriteTo.File(
                Path.Combine(logDir, "misc.log"),
                outputTemplate: FileTemplate,
                flushToDiskInterval: TimeSpan.FromSeconds(1)));

        return loggerConfiguration.CreateLogger();
    }

    public static void CaptureUnhandledExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Fatal(ex, "Unhandled exception (terminating: {IsTerminating})", e.IsTerminating);
            else
                Log.Fatal("Unhandled non-exception throw (terminating: {IsTerminating})", e.IsTerminating);

            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += static (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }

    public static LogEventLevel ParseLevel(string? levelName, LogEventLevel fallback)
    {
        if (string.IsNullOrWhiteSpace(levelName))
            return fallback;

        if (string.Equals(levelName, "Trace", StringComparison.OrdinalIgnoreCase))
            return LogEventLevel.Verbose;

        return Enum.TryParse<LogEventLevel>(levelName, ignoreCase: true, out var level)
            ? level
            : fallback;
    }

    private static bool ShouldWriteToConsole(
        LogEvent logEvent,
        IReadOnlyCollection<string> consoleSourcePrefixes,
        LogEventLevel consoleLevel)
    {
        return logEvent.Level >= consoleLevel
            || (logEvent.Level >= LogEventLevel.Debug && MatchesAnySource(logEvent, consoleSourcePrefixes));
    }

    private static bool MatchesAnySource(LogEvent logEvent, IReadOnlyCollection<string> sourcePrefixes)
    {
        if (sourcePrefixes.Count == 0)
            return false;

        if (!logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value))
            return false;

        var sourceContext = value.ToString().Trim('"');
        return sourcePrefixes.Any(prefix => sourceContext.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
