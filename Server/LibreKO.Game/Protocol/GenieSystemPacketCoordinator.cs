using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IGenieSystemPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
    Task StartAsync(UserSession session);
    Task StopAsync(UserSession session);
}

public class GenieSystemPacketCoordinator(
    SessionManager sessionManager,
    IMagicItemUsageService itemUsage,
    ICombatPacketCoordinator combat,
    IMagicPacketCoordinator magic,
    IWorldPacketCoordinator world,
    ILogger<GenieSystemPacketCoordinator> logger) : IGenieSystemPacketCoordinator
{
    public const int SpiritOfGenieItem = 810378000;
    public const int SpiritOfGenieMinutes = 120;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var request = packet.ReadByte();
        if (packet.RemainingBytes < 1)
            return;

        if (request == GenieSystemPacketWriter.UpdateRequest)
        {
            await RelayAsync(client, session, packet);
            return;
        }

        if (request != GenieSystemPacketWriter.InfoRequest)
            return;

        switch (packet.ReadByte())
        {
            case GenieSystemPacketWriter.UseSpiritPotion:
                await UseSpiritPotionAsync(session);
                break;
            case GenieSystemPacketWriter.LoadOptions:
                await session.Client.SendPacket(
                    GenieSystemPacketWriter.Options(session.GenieOptions));
                break;
            case GenieSystemPacketWriter.SaveOptions:
                SaveOptions(session, packet);
                break;
            case GenieSystemPacketWriter.Start:
                await StartAsync(session);
                break;
            case GenieSystemPacketWriter.Stop:
                await StopAsync(session);
                break;
        }
    }

    public async Task StartAsync(UserSession session)
    {
        if (session.GenieMinutes == 0)
        {
            await StopAsync(session);
            return;
        }

        if (session.GenieActive)
            return;

        session.GenieActive = true;
        await session.Client.SendPacket(GenieSystemPacketWriter.Started(session.GenieMinutes));
        await BroadcastActivationAsync(session);
    }

    public async Task StopAsync(UserSession session)
    {
        var wasActive = session.GenieActive;
        session.GenieActive = false;
        await session.Client.SendPacket(GenieSystemPacketWriter.Stopped(session.GenieMinutes));

        if (wasActive)
            await BroadcastActivationAsync(session);
    }

    private async Task RelayAsync(IClient client, UserSession session, Packet packet)
    {
        if (session.GenieMinutes == 0)
        {
            await StopAsync(session);
            return;
        }

        switch (packet.ReadByte())
        {
            case GenieSystemPacketWriter.Move:
                await world.HandleMoveAsync(client, packet);
                break;
            case GenieSystemPacketWriter.Rotate:
                await world.HandleRotateAsync(client, packet);
                break;
            case GenieSystemPacketWriter.MainAttack:
                await combat.HandleAttackAsync(client, packet);
                break;
            case GenieSystemPacketWriter.Magic:
                await magic.HandleAsync(client, packet);
                break;
            default:
                logger.LogDebug("Unhandled genie action from {Name}", session.Name);
                break;
        }
    }

    private async Task UseSpiritPotionAsync(UserSession session)
    {
        if (!await itemUsage.TryConsumeItemAsync(session, SpiritOfGenieItem))
            return;

        var standing = session.GenieExpiry > DateTime.UtcNow
            ? session.GenieExpiry!.Value
            : DateTime.UtcNow;
        session.GenieExpiry = standing.AddMinutes(SpiritOfGenieMinutes);

        await session.Client.SendPacket(
            GenieSystemPacketWriter.SpiritPotion(session.GenieMinutes));
    }

    private static void SaveOptions(UserSession session, Packet packet)
    {
        var options = new byte[GenieSystemPacketWriter.OptionBytes];
        var available = Math.Min(packet.RemainingBytes, options.Length);
        for (var i = 0; i < available; i++)
            options[i] = packet.ReadByte();
        session.GenieOptions = options;
    }

    private Task BroadcastActivationAsync(UserSession session) =>
        sessionManager.Regions.SendToRegion(
            session,
            GenieSystemPacketWriter.ActivationState((ushort)session.CharacterId, session.GenieActive),
            excludeSender: false);
}
