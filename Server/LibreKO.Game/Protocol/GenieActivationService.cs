using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IGenieActivationService
{
    Task StartAsync(UserSession session);
    Task StopAsync(UserSession session);
}

public class GenieActivationService(SessionManager sessionManager) : IGenieActivationService
{
    public async Task StartAsync(UserSession session)
    {
        if (session.GenieMinutes == 0 || session.Hp <= 0)
        {
            await StopAsync(session);
            return;
        }

        if (session.GenieActive)
        {
            await session.Client.SendPacket(GenieSystemPacketWriter.Started(session.GenieMinutes));
            return;
        }

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

    private Task BroadcastActivationAsync(UserSession session) =>
        sessionManager.Regions.SendToRegion(
            session,
            GenieSystemPacketWriter.ActivationState(session.CharacterId, session.GenieActive),
            excludeSender: false);
}
