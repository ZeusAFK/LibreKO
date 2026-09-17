using System.Collections.Concurrent;
using LibreKO.Common.Enums;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Configuration;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game;

public class GamePacketHandler(
    IServiceProvider serviceProvider,
    SessionManager sessionManager,
    ISessionTerminationService sessionTerminationService,
    IAccountLockService accountLockService,
    IZoneTransitionService zoneTransitionService,
    IAdminPacketCoordinator adminPacketCoordinator,
    IAdminPanelPacketCoordinator adminPanelPacketCoordinator,
    IChatPacketCoordinator chatPacketCoordinator,
    IMiscPacketCoordinator miscPacketCoordinator,
    IWorldPacketCoordinator worldPacketCoordinator,
    IShoppingMallPacketCoordinator shoppingMallPacketCoordinator,
    ISavedMagicService savedMagicService,
    IInGameOpcodeRouter opcodeRouter,
    ILogger<GamePacketHandler> logger) : IPacketHandler
{
    private const byte LoginFollowUpOpcode = 0xC0;

    private const int PingMinIntervalMs = 200;
    private const int PingMaxEchoBytes = 8;
    private readonly ConcurrentDictionary<Guid, long> _lastPingTicks = new();

    public async Task OnClientDisconnected(IClient client)
    {
        _lastPingTicks.TryRemove(client.Id, out _);
        await sessionTerminationService.DisconnectAsync(client);
        await accountLockService.ReleaseAsync(client);
    }

    private async Task HandlePingAsync(IClient client, Packet packet)
    {
        var now = Environment.TickCount64;
        if (now - _lastPingTicks.GetValueOrDefault(client.Id) < PingMinIntervalMs)
            return;
        _lastPingTicks[client.Id] = now;

        var echoBytes = Math.Min(packet.RemainingBytes, PingMaxEchoBytes);
        await client.SendPacket(SessionPacketWriter.Pong(
            echoBytes > 0 ? packet.ReadBytes(echoBytes) : []));
    }

    public async Task HandlePacket(IClient client, Packet packet)
    {
        var opcode = (GameOpcodes)packet.GetOpcode();

        if (opcode == GameOpcodes.GS_COMPRESS_PACKET)
        {
            var decompressed = Packet.Decompress(packet);
            if (decompressed != null)
                await HandlePacket(client, decompressed);
            return;
        }

        if (opcode == GameOpcodes.GS_PING)
        {
            await HandlePingAsync(client, packet);
            return;
        }

        // In-game traffic (movement, combat, …) is the overwhelming majority, and its
        // handlers are singleton coordinators that scope their own DB work. Skip the
        // per-packet DI scope + DbContext entirely for it — only the rare per-connection
        // auth / pre-game / game-start paths need scoped services.
        if (client.AccountId != 0 && client.CharacterId != 0)
        {
            await HandleInGamePacket(client, packet, opcode);
            return;
        }

        using var scope = serviceProvider.CreateScope();

        if (client.AccountId == 0)
        {
            if (opcode == GameOpcodes.GS_VERSION_CHECK)
            {
                var settings = scope.ServiceProvider.GetRequiredService<IOptions<GameServerSettings>>();
                await HandleVersionCheckAsync(client, settings);
                return;
            }
            if (opcode == GameOpcodes.GS_LOGIN)
            {
                var preGameService = scope.ServiceProvider.GetRequiredService<IPreGameService>();
                var response = await preGameService.LoginAsync(packet.ReadString(), packet.ReadString());
                await HandleLoginAsync(client, response);
                return;
            }
            if (opcode == GameOpcodes.GS_KICKOUT)
            {
                var preGameService = scope.ServiceProvider.GetRequiredService<IPreGameService>();
                await HandleKickOutAsync(client, packet, preGameService);
            }
            return;
        }

        // CharacterId == 0: pre-game (character select, etc.)
        var preGamePacketCoordinator = scope.ServiceProvider.GetRequiredService<IPreGamePacketCoordinator>();
        await HandlePreGamePacket(client, packet, opcode, preGamePacketCoordinator);
    }

    private async Task HandlePreGamePacket(IClient client, Packet packet, GameOpcodes opcode, IPreGamePacketCoordinator preGamePacketCoordinator)
    {
        var response = await preGamePacketCoordinator.HandleAsync(client, packet, opcode);

        if (response == null && !ShouldSuppressPreGameWarning(opcode, packet))
            logger.LogWarning(
                "Unhandled opcode 0x{Opcode:X2} from client {ClientId} (pre-game), payload={Payload}",
                packet.GetOpcode(),
                client.Id,
                Convert.ToHexString(packet.GetData()));

        if (response != null)
            await client.SendPacket(response);
    }

    private async Task HandleLoginAsync(IClient client, GameLoginResult response)
    {
        if (!response.Success)
        {
            await client.SendPacket(SessionPacketWriter.LoginDenied());
            return;
        }

        var claim = await accountLockService.AcquireAsync(client, response.AccountId);
        if (!claim.Granted && claim.Occupant != null)
        {
            logger.LogInformation(
                "Refused login for account {AccountId} from client {ClientId}: already connected ({Stage})",
                response.AccountId, client.Id, claim.Occupant.Stage);

            await client.SendPacket(SessionPacketWriter.LoginDeniedAccountInUse(claim.Occupant));
            return;
        }

        client.AccountId = response.AccountId;

        await client.SendPacket(SessionPacketWriter.LoginAccepted((byte)response.Nation));
        await client.SendPacket(SessionPacketWriter.LoginFollowUp());
    }

    private async Task HandleKickOutAsync(IClient client, Packet packet, IPreGameService preGameService)
    {
        var login = packet.ReadString();
        var password = packet.ReadString();

        var auth = await preGameService.LoginAsync(login, password);
        if (!auth.Success)
        {
            logger.LogWarning("Rejected kick request for '{Login}' from client {ClientId}", login, client.Id);
            await client.SendPacket(SessionPacketWriter.KickResult(AccountKickCode.Rejected));
            return;
        }

        var code = await accountLockService.KickAsync(auth.AccountId);
        logger.LogInformation("Kick request for account {AccountId} from client {ClientId}: {Code}",
            auth.AccountId, client.Id, code);

        await client.SendPacket(SessionPacketWriter.KickResult(code));
    }

    private static bool ShouldSuppressPreGameWarning(GameOpcodes opcode, Packet packet)
    {
        if (opcode is GameOpcodes.GS_SPEEDHACK_CHECK or GameOpcodes.GS_HACKTOOL or GameOpcodes.GS_REPORT_BUG)
            return true;

        if (opcode != GameOpcodes.GS_ALLCHAR_INFO_REQ || packet.GetLength() <= 0)
            return false;

        return packet.GetData()[0] is (byte)AllCharacterInfoOpcode.ArrangeOpen or (byte)AllCharacterInfoOpcode.ArrangeReceive;
    }

    private async Task HandleInGamePacket(
        IClient client,
        Packet packet,
        GameOpcodes opcode)
    {
        switch (opcode)
        {
            case GameOpcodes.GS_GAMESTART:
                {
                    // Rare (per login) — the only in-game opcode needing scoped services.
                    using var scope = serviceProvider.CreateScope();
                    var preGameService = scope.ServiceProvider.GetRequiredService<IPreGameService>();
                    var gameSessionInitializer = scope.ServiceProvider.GetRequiredService<IGameSessionInitializer>();
                    await HandleGameStartAsync(client, packet, preGameService, gameSessionInitializer);
                }
                return;

            case GameOpcodes.GS_CHAT:
                await HandleChatAsync(client, packet);
                return;
        }

        var handler = opcodeRouter.Resolve(opcode);
        if (handler != null)
        {
            await handler(client, packet);
        }
        else
        {
            logger.LogDebug("Unhandled in-game opcode 0x{Opcode:X2} from client {ClientId}", packet.GetOpcode(), client.Id);
        }
    }

    private async Task HandleGameStartAsync(
        IClient client,
        Packet packet,
        IPreGameService preGameService,
        IGameSessionInitializer gameSessionInitializer)
    {
        var subOpcode = packet.ReadByte();
        UserSession? session = null;

        if (!accountLockService.Owns(client))
        {
            logger.LogWarning("Ignoring game start from client {ClientId}: it no longer holds its account claim", client.Id);
            return;
        }

        if (subOpcode == 1)
        {
            session = await gameSessionInitializer.InitializeAsync(client);
            if (session == null)
                return;

            logger.LogDebug(
                "GameStart subOp=1: {Name} (id={Id}) entered zone={Zone} pos=({X},{Z}) region=({RX},{RZ})",
                session.Name, session.CharacterId, session.ZoneId, session.X, session.Z, session.RegionX, session.RegionZ);
        }

        var responses = await preGameService.GameStartAsync(client.CharacterId, client.AccountId, subOpcode, session);
        foreach (var response in responses)
        {
            await client.SendPacket(response);

            if (subOpcode == 1
                && session != null
                && response.GetOpcode() == (byte)GameOpcodes.GS_MYINFO)
            {
                await zoneTransitionService.SendZoneAbilityAsync(session);
                await adminPanelPacketCoordinator.SendGrantAsync(session);
                await worldPacketCoordinator.SendRegionUserListAsync(session);
                await worldPacketCoordinator.SendNpcRegionListAsync(session);
            }
        }

        if (subOpcode == 2)
        {
            session ??= sessionManager.GetByClientId(client.Id) ?? await gameSessionInitializer.InitializeAsync(client);
            if (session == null)
                return;

            logger.LogDebug(
                "GameStart subOp=2: {Name} (id={Id}) ready in zone={Zone} pos=({X},{Z}) region=({RX},{RZ}) — broadcasting Respawn",
                session.Name, session.CharacterId, session.ZoneId, session.X, session.Z, session.RegionX, session.RegionZ);

            await zoneTransitionService.SendZoneAbilityAsync(session);
            if (session.AccountStatus != 0 || session.PremiumType != 0 || session.PremiumTime > 0)
                await miscPacketCoordinator.SendPremiumInfoAsync(session);
            await worldPacketCoordinator.BroadcastUserInOutAsync(session, InOutType.Respawn);
            await shoppingMallPacketCoordinator.SendUnreadAsync(session);
            await savedMagicService.RecastAsync(session);

            if (session.Hp <= 0)
                await SendReconnectDeathStateAsync(session);
        }
    }

    private static async Task SendReconnectDeathStateAsync(UserSession session)
    {
        await session.Client.SendPacket(DeathPacketWriter.PlayerDeath(
            session.CharacterId, DeathPacketWriter.NoKiller));
    }

    private static async Task HandleVersionCheckAsync(IClient client, IOptions<GameServerSettings> settings)
    {
        await client.SendPacket(SessionPacketWriter.VersionCheck((short)settings.Value.Version));
    }

    private async Task HandleChatAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var chatType = packet.ReadByte();
        var message = packet.ReadString();

        logger.LogDebug("Chat from {Name}: IsGM={IsGM}, message={Message}", session.Name, session.IsGM, message);
        if (session.IsGM && message.StartsWith('+'))
        {
            logger.LogInformation("GM command from {Name}: {Message}", session.Name, message);
            await adminPacketCoordinator.HandleGmCommandAsync(session, message);
            return;
        }
        await chatPacketCoordinator.HandleAsync(session, chatType, message);
    }
}
