using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Login.Enums;

namespace LibreKO.Login.Protocol.Writers;

public sealed class LoginPacketWriter
{
    public const short NoPremium = -1;
    public const short ServerFull = -1;
    public const short NoExtraSockets = 0;
    public const int NewsTitleDisconnects = 255;
    public const char ServerGroupSeparator = '|';

    public static Packet Cryption(byte[] key)
    {
        var packet = new Packet(LoginOpcodes.LS_CRYPTION);
        packet.WriteByte((byte)key.Length);
        packet.WriteBytes(key);
        return packet;
    }

    public static Packet VersionCheck(short version)
    {
        var packet = new Packet(LoginOpcodes.LS_VERSION_REQ);
        packet.WriteShort(version);
        return packet;
    }

    public static Packet DownloadInfo(string ftpUrl, string ftpPath, IReadOnlyCollection<string> fileNames)
    {
        var packet = new Packet(LoginOpcodes.LS_DOWNLOADINFO_REQ);
        packet.WriteString(ftpUrl);
        packet.WriteString(ftpPath);
        packet.WriteShort((short)fileNames.Count);

        foreach (var fileName in fileNames)
            packet.WriteString(fileName);

        return packet;
    }

    public static Packet LoginSucceeded(
        LoginOpcodes responseOpcode,
        LoginResult result,
        short remainingPremiumHours,
        string login,
        GameLanguage language = GameLanguage.English)
    {
        var packet = LoginResultHeader(responseOpcode, result);
        packet.WriteShort(remainingPremiumHours > 0 ? remainingPremiumHours : NoPremium);
        packet.WriteString(login);
        packet.WriteUInt(GameplayProtocol.AccountExtraMagic);
        packet.WriteByte(GameplayProtocol.ExtensionVersion);
        packet.WriteByte((byte)language);
        return packet;
    }

    public static Packet LoginOccupied(
        LoginOpcodes responseOpcode, LoginResult result, Server? server)
    {
        var packet = LoginResultHeader(responseOpcode, result);
        packet.WriteString(server?.IpAddress ?? string.Empty);
        packet.WriteUShort((ushort)(server?.Port ?? 0));
        packet.WriteUInt((uint)(server?.Id ?? 0));
        packet.WriteString(server?.Name ?? string.Empty);
        packet.WriteUInt(GameplayProtocol.AccountLockMagic);
        packet.WriteByte(GameplayProtocol.ExtensionVersion);
        packet.WriteInt(server?.Port ?? 0);
        packet.WriteString(server?.LanIpAddress ?? string.Empty);
        return packet;
    }

    public static Packet LoginRejected(LoginOpcodes responseOpcode, LoginResult result) =>
        LoginResultHeader(responseOpcode, result);

    public readonly record struct ServerListEntry(
        short Id,
        string GroupName,
        string Name,
        short GroupId,
        ServerCategory Category,
        string Address,
        string LanAddress,
        short OnlinePlayers,
        short MaxPlayers,
        short FreePlayerCap,
        int Port,
        string KarusKing,
        string KarusNotice,
        string ElMoradKing,
        string ElMoradNotice);

    public static Packet ServerList(short echo, IReadOnlyList<ServerListEntry> servers)
    {
        var packet = new Packet(LoginOpcodes.LS_SERVERLIST);
        packet.WriteShort(echo);
        packet.WriteByte((byte)servers.Count);

        foreach (var server in servers)
        {
            packet.WriteString(server.Address);
            packet.WriteString(server.LanAddress);
            packet.WriteString(QualifiedName(server));
            packet.WriteShort(server.OnlinePlayers <= server.MaxPlayers ? server.OnlinePlayers : ServerFull);
            packet.WriteShort(server.Id);
            packet.WriteShort(server.GroupId);
            packet.WriteShort(server.MaxPlayers);
            packet.WriteShort(server.FreePlayerCap);
            packet.WriteByte(0);
            packet.WriteByte((byte)server.Category);
            packet.WriteString(server.KarusKing);
            packet.WriteString(server.KarusNotice);
            packet.WriteString(server.ElMoradKing);
            packet.WriteString(server.ElMoradNotice);
        }

        packet.WriteUInt(GameplayProtocol.ServerListMagic);
        packet.WriteByte(GameplayProtocol.ExtensionVersion);
        packet.WriteByte((byte)servers.Count);

        foreach (var server in servers)
        {
            packet.WriteShort(server.Id);
            packet.WriteInt(server.Port);
        }

        return packet;
    }

    private static string QualifiedName(ServerListEntry server) =>
        server.GroupName.Length > 0
            ? $"{server.GroupName}{ServerGroupSeparator}{server.Name}"
            : server.Name;

    public static Packet News(string title, string body)
    {
        var packet = new Packet(LoginOpcodes.LS_NEWS);
        packet.WriteString(title.Length == NewsTitleDisconnects ? title[..^1] : title);
        packet.WriteString(body);
        return packet;
    }

    public static Packet UnknownF7()
    {
        var packet = new Packet(LoginOpcodes.LS_UNKNOWN_F7);
        packet.WriteShort(0);
        return packet;
    }

    public readonly record struct LauncherNotice(string Text, string Link);

    public static Packet LauncherNews(IReadOnlyList<LauncherNotice> notices)
    {
        var packet = new Packet(LoginOpcodes.LS_LAUNCHER_NEWS);
        packet.WriteShort((short)notices.Count);

        foreach (var notice in notices)
        {
            packet.WriteString(notice.Text);
            packet.WriteString(notice.Link);
        }

        return packet;
    }

    public static Packet SocketList()
    {
        var packet = new Packet(LoginOpcodes.LS_SOCKET_LIST);
        packet.WriteShort(NoExtraSockets);
        return packet;
    }

    private static Packet LoginResultHeader(LoginOpcodes responseOpcode, LoginResult result)
    {
        var packet = new Packet(responseOpcode);
        packet.WriteString(string.Empty);
        packet.WriteByte((byte)result);
        return packet;
    }
}
