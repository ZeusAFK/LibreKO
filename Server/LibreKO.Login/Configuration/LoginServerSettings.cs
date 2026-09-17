using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Login.Configuration;

public class LoginServerSettings
{
    public const string SectionName = "LoginServer";

    public string BindHost { get; set; } = "*";
    public int BindPort { get; set; } = default!;
    public int Version { get; set; } = default!;
    public FtpSettings Ftp { get; set; } = new();
    public AccountSettings Account { get; set; } = new();

    public ConnectionLimitsSettings Connections { get; set; } = new();
}

public class FtpSettings
{
    public string Url { get; set; } = "127.0.0.1";
    public string Path { get; set; } = "/";
}

public class AccountSettings
{
    public bool AutoCreate { get; set; }
}
