namespace LibreKO.Common.Infrastructure.Logging;

public sealed class ErrorTrackingOptions
{
    public ErrorTrackingOptions(
        string dsn,
        string? release = null,
        string? environment = null,
        string? serverName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dsn);

        Dsn = dsn;
        Release = release;
        Environment = environment;
        ServerName = serverName;
    }

    public string Dsn { get; }

    public string? Release { get; }

    public string? Environment { get; }

    public string? ServerName { get; }

    public static ErrorTrackingOptions? FromConfiguration(
        string? dsn,
        string? release,
        string? environment,
        string serverName)
    {
        return string.IsNullOrWhiteSpace(dsn)
            ? null
            : new ErrorTrackingOptions(dsn, release, environment, serverName);
    }
}
