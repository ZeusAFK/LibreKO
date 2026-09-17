using LibreKO.Common.Infrastructure.Logging;

namespace LibreKO.Login.Startup;

public static class SerilogSetup
{
    private static readonly LogFileRoute[] Routes =
    [
        new("startup.log", "Microsoft.Hosting.Lifetime"),
        new("network.log", "LibreKO.Common.Infrastructure.Network"),
        new("auth.log", "LibreKO.Login.Commands"),
        new("packets.log", "LibreKO.Login.LoginPacketHandler")
    ];

    private static readonly string[] ConsoleSourcePrefixes =
    [
        "LibreKO.Common.Infrastructure.Network.SocketServer",
        "LibreKO.Common.Infrastructure.Network",
        "LibreKO.Login.LoginPacketHandler",
        "LibreKO.Login.LoginService",
        "Microsoft.Hosting.Lifetime"
    ];

    public static Serilog.ILogger CreateLogger(
        string baseDir,
        string? fileLevelName = null,
        string? consoleLevelName = null,
        int retentionDays = 10,
        ErrorTrackingOptions? errorTracking = null)
    {
        return SerilogHostLogging.CreateLogger(
            baseDir,
            Routes,
            ConsoleSourcePrefixes,
            fileLevelName,
            consoleLevelName,
            retentionDays,
            errorTracking);
    }
}
