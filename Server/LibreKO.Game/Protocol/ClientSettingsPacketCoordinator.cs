using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IClientSettingsPacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class ClientSettingsPacketCoordinator(
    SessionManager sessionManager,
    ICharacterStatePersister statePersister,
    ILogger<ClientSettingsPacketCoordinator> logger) : IClientSettingsPacketCoordinator
{
    public const byte SubGetLanguage = 1;
    public const byte SubSetLanguage = 2;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var sub = packet.ReadByte();
        switch (sub)
        {
            case SubGetLanguage:
                await client.SendPacket(ClientSettingsPacketWriter.Language(session.Language, accepted: true));
                return;

            case SubSetLanguage:
                await SetLanguageAsync(client, session, packet);
                return;

            default:
                logger.LogDebug("Unknown GS_CLIENT_SETTINGS sub-opcode {Sub} from {Name}", sub, session.Name);
                return;
        }
    }

    private async Task SetLanguageAsync(IClient client, UserSession session, Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return;

        var requested = packet.ReadByte();
        if (!Enum.IsDefined(typeof(GameLanguage), requested))
        {
            logger.LogInformation(
                "{Name} asked for language {Requested}, which this server does not have; keeping {Current}",
                session.Name, requested, session.Language);
            await client.SendPacket(ClientSettingsPacketWriter.Language(session.Language, accepted: false));
            return;
        }

        var language = (GameLanguage)requested;
        if (language == session.Language)
        {
            await client.SendPacket(ClientSettingsPacketWriter.Language(language, accepted: true));
            return;
        }

        session.Language = language;
        _ = statePersister.SaveAsync(session);

        logger.LogInformation("{Name} switched language to {Language}", session.Name, language);
        await client.SendPacket(ClientSettingsPacketWriter.Language(language, accepted: true));
    }
}
