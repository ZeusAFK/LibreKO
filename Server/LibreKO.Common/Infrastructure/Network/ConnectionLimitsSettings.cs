namespace LibreKO.Common.Infrastructure.Network;

public class ConnectionLimitsSettings
{
    public int MaxConnectionsPerIp { get; set; } = 10;
    public int ConnectionRateWindowSeconds { get; set; } = 10;
    public int MaxConnectionAttemptsPerWindow { get; set; } = 20;
}
