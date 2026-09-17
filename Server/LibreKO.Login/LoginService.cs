using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Login.Configuration;
using LibreKO.Login.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using LibreKO.Login.Protocol.Writers;

namespace LibreKO.Login;

public interface ILoginService
{
    Task<Packet> VersionCheckAsync();
    Task<Packet> DownloadInfoAsync(short version);
    Task<Packet> LoginAsync(string login, string password, LoginOpcodes responseOpcode = LoginOpcodes.LS_LOGIN, LoginRequestFlags flags = LoginRequestFlags.None);
    Task<Packet> ServerListAsync(short echo);
    Task<Packet> NewsAsync();
    Task<Packet> UnknownF7Async();
    Task<Packet> LauncherNewsAsync();
    Task<Packet> SocketListAsync();
}

public class LoginService(
    IAccountRepository accountRepository,
    IServerRepository serverRepository,
    IPatchRepository patchRepository,
    IKingRepository kingRepository,
    IOptions<LoginServerSettings> settings,
    ILogger<LoginService> logger) : ILoginService
{
    private const string NewsTitle = "LoginNotice";
    private const string NewsBody = "<empty>";

    public Task<Packet> VersionCheckAsync()
    {
        var packet = LoginPacketWriter.VersionCheck((short)settings.Value.Version);

        return Task.FromResult(packet);
    }

    public async Task<Packet> DownloadInfoAsync(short version)
    {
        var ftpSettings = settings.Value.Ftp;
        var patchList = await patchRepository.GetPatchList();
        var fileNames = patchList
            .Where(patch => patch.FileId > version)
            .Select(patch => patch.FileName)
            .ToList();

        return LoginPacketWriter.DownloadInfo(ftpSettings.Url, ftpSettings.Path, fileNames);
    }

    public async Task<Packet> LoginAsync(string login, string password, LoginOpcodes responseOpcode = LoginOpcodes.LS_LOGIN, LoginRequestFlags flags = LoginRequestFlags.None)
    {
        var account = await accountRepository.GetByLogin(login);
        short remainingPremiumHours = 0;
        Server? occupiedServer = null;

        LoginResult result;
        if (account == null)
        {
            // Auto-create account if enabled
            if (settings.Value.Account.AutoCreate)
            {
                account = new Account
                {
                    Login = login,
                    Password = PasswordHasher.Hash(password),
                    Authority = AccountAuthority.Normal,
                    Nation = AccountNation.None,
                    AccessDate = DateTime.UtcNow
                };
                await accountRepository.CreateAsync(account);
                result = LoginResult.Success;
                remainingPremiumHours = account.RemainingPremiumHours;
                logger.LogInformation("Auto-created and logged in new account: {Login}", login);
            }
            else
            {
                result = LoginResult.IdNotFound;
                logger.LogWarning("Login attempt with non-existing account: {Login}", login);
            }
        }
        else if (!PasswordHasher.Verify(password, account.Password))
        {
            result = LoginResult.InvalidPassword;
            logger.LogWarning("Invalid password attempt for account: {Login}", login);
        }
        else if (account.Authority == AccountAuthority.Banned)
        {
            result = LoginResult.AccountBlocked;
            logger.LogWarning("Banned account login attempt: {Login}", login);
        }
        else if (account.OnlineServerId is { } onlineServerId
                 && !flags.HasFlag(LoginRequestFlags.IgnoreOnlineClaim))
        {
            result = LoginResult.AlreadyInGame;
            occupiedServer = (await serverRepository.GetServers())
                .FirstOrDefault(server => server.Id == onlineServerId);
            logger.LogInformation(
                "Account {Login} is already connected to server {ServerId} since {Since:u}",
                login, onlineServerId, account.OnlineSince);
        }
        else
        {
            if (account.OnlineServerId != null)
            {
                logger.LogInformation(
                    "Clearing the online claim on account {Login} at the player's request (server {ServerId} did not answer)",
                    login, account.OnlineServerId);
                await accountRepository.ClearOnlineServerAsync(account.Id);
            }

            result = LoginResult.Success;
            logger.LogInformation("Account logged in successfully: {Login}", login);
            remainingPremiumHours = account.RemainingPremiumHours;
        }

        return result switch
        {
            LoginResult.Success => LoginPacketWriter.LoginSucceeded(
                responseOpcode, result, remainingPremiumHours, login,
                account?.Language ?? GameLanguage.English),
            LoginResult.AlreadyInGame => LoginPacketWriter.LoginOccupied(
                responseOpcode, result, occupiedServer),
            _ => LoginPacketWriter.LoginRejected(responseOpcode, result),
        };
    }

    public async Task<Packet> ServerListAsync(short echo)
    {
        var servers = await serverRepository.GetServers();
        var kings = await kingRepository.GetKingsAsync();
        var karus = kings.FirstOrDefault(king => king.Nation == (byte)AccountNation.Karus);
        var elMorad = kings.FirstOrDefault(king => king.Nation == (byte)AccountNation.ElMorad);

        var entries = servers
            .Select(server => new LoginPacketWriter.ServerListEntry(
                Id: (short)server.Id,
                GroupName: server.Group?.Name ?? string.Empty,
                Name: server.Name,
                GroupId: (short)(server.GroupId ?? 0),
                Category: server.Category,
                Address: server.IpAddress,
                LanAddress: server.LanIpAddress,
                OnlinePlayers: (short)server.OnlinePlayers,
                MaxPlayers: (short)server.MaxPlayers,
                FreePlayerCap: (short)(server.FreePlayerCap > 0 ? server.FreePlayerCap : server.MaxPlayers),
                Port: server.Port,
                KarusKing: karus?.KingName ?? string.Empty,
                KarusNotice: karus?.Notice ?? string.Empty,
                ElMoradKing: elMorad?.KingName ?? string.Empty,
                ElMoradNotice: elMorad?.Notice ?? string.Empty))
            .ToList();

        return LoginPacketWriter.ServerList(echo, entries);
    }

    public Task<Packet> NewsAsync()
    {
        return Task.FromResult(LoginPacketWriter.News(NewsTitle, NewsBody));
    }

    public Task<Packet> UnknownF7Async()
    {
        return Task.FromResult(LoginPacketWriter.UnknownF7());
    }

    public Task<Packet> LauncherNewsAsync()
    {
        return Task.FromResult(LoginPacketWriter.LauncherNews([]));
    }

    public Task<Packet> SocketListAsync()
    {
        return Task.FromResult(LoginPacketWriter.SocketList());
    }
}
