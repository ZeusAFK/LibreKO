namespace LibreKO.Network;

public sealed class ServerListEntry
{
    public string Name = "";
    public string Group = "";
    public string LanIp = "";
    public string WanIp = "";
    public short Players;
    public short Id;
    public short MaxPlayers;
    public int Port;

    public string Address => WanIp.Length > 0 ? WanIp : LanIp;
}

public static class LoginWire
{
    public const char ServerGroupSeparator = '|';

    private const int ExtensionHeaderBytes = 5;
    private const int ServerListExtensionHeaderBytes = 6;
    private const int ServerPortRecordBytes = 6;
    private const int MaxPort = 65535;

    public static List<ServerListEntry> ReadServerList(Packet p, int defaultPort)
    {
        p.ReadShort();
        int count = p.ReadByte();
        var list = new List<ServerListEntry>(count);
        for (int i = 0; i < count; i++)
        {
            var entry = new ServerListEntry
            {
                WanIp = p.ReadString(),
                LanIp = p.ReadString(),
                Port = defaultPort,
            };
            SplitServerName(p.ReadString(), entry);
            entry.Players = p.ReadShort();
            entry.Id = p.ReadShort();
            p.ReadShort();
            entry.MaxPlayers = p.ReadShort();
            p.ReadShort();
            p.ReadByte();
            p.ReadByte();
            p.ReadString();
            p.ReadString();
            p.ReadString();
            p.ReadString();
            list.Add(entry);
        }

        if (p.RemainingBytes >= ServerListExtensionHeaderBytes && p.ReadUInt() == LibreKOProtocol.ServerListMagic)
        {
            byte extensionVersion = p.ReadByte();
            int extensionCount = p.ReadByte();
            if (extensionVersion == LibreKOProtocol.ExtensionVersion)
            {
                var byId = new Dictionary<short, ServerListEntry>();
                foreach (var entry in list)
                    byId[entry.Id] = entry;

                for (int i = 0; i < extensionCount && p.RemainingBytes >= ServerPortRecordBytes; i++)
                {
                    short serverId = p.ReadShort();
                    int port = p.ReadInt();
                    if (port is >= 1 and <= MaxPort && byId.TryGetValue(serverId, out var entry))
                        entry.Port = port;
                }
            }
        }
        return list;
    }

    public static GameLanguage? ReadAccountLanguage(Packet p)
    {
        p.ReadShort();
        p.ReadString();
        if (p.RemainingBytes < ExtensionHeaderBytes + 1
            || p.ReadUInt() != LibreKOProtocol.AccountExtraMagic
            || p.ReadByte() != LibreKOProtocol.ExtensionVersion)
            return null;
        return LibreKOProtocol.ToLanguage(p.ReadByte());
    }

    public static AccountInUse ReadOccupiedServer(Packet p, string defaultHost, int defaultPort)
    {
        var info = new AccountInUse { Stage = AccountClaimStage.InGame };
        try
        {
            info.Host = p.ReadString();
            info.Port = p.ReadUShort();
            info.ServerId = (int)p.ReadUInt();
            info.ServerName = p.ReadString();

            if (p.RemainingBytes >= ExtensionHeaderBytes
                && p.ReadUInt() == LibreKOProtocol.AccountLockMagic
                && p.ReadByte() == LibreKOProtocol.ExtensionVersion)
            {
                int port = p.ReadInt();
                if (port is >= 1 and <= MaxPort)
                    info.Port = port;
                string lanIp = p.ReadString();
                if (info.Host.Length == 0)
                    info.Host = lanIp;
            }
        }
        catch (Exception e)
        {
            Diag.Report("account-in-use parse", e);
        }

        if (info.Host.Length == 0) info.Host = defaultHost;
        if (info.Port <= 0) info.Port = defaultPort;
        return info;
    }

    private static void SplitServerName(string wireName, ServerListEntry entry)
    {
        int pipe = wireName.IndexOf(ServerGroupSeparator);
        if (pipe < 0)
        {
            entry.Name = wireName;
            return;
        }
        entry.Group = wireName[..pipe];
        entry.Name = wireName[(pipe + 1)..];
    }
}
