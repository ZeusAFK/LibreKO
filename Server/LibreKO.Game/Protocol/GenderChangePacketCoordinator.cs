using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.Protocol;

public interface IGenderChangePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public class GenderChangePacketCoordinator(
    IServiceScopeFactory scopeFactory,
    SessionManager sessionManager,
    IMagicItemUsageService magicItemUsageService,
    IZoneTransitionService zoneTransitionService,
    ILogger<GenderChangePacketCoordinator> logger) : IGenderChangePacketCoordinator
{
    public const int GenderChangeItem = 810594000;
    private const byte ChangeRequest = 1;
    private const int RequestLength = 7;

    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < RequestLength || packet.ReadByte() != ChangeRequest)
            return;

        var race = packet.ReadByte();
        var face = packet.ReadByte();
        var hair = packet.ReadInt();

        if (session.Hp <= 0 || session.Trade.IsTrading || session.Trade.IsMerchanting || session.IsGathering
            || !GenderChangeRules.Allows(session.Class, race))
        {
            await client.SendPacket(GenderChangePacketWriter.Result(GenderChangePacketWriter.Failed));
            return;
        }

        if (!magicItemUsageService.CanUseItem(session, GenderChangeItem))
        {
            await client.SendPacket(GenderChangePacketWriter.Result(GenderChangePacketWriter.NoItem));
            return;
        }

        var oldRace = session.Race;
        session.Race = race;
        session.Face = face;
        session.Hair = hair;
        await magicItemUsageService.TryConsumeItemAsync(session, GenderChangeItem);
        await PersistAsync(session);

        logger.LogInformation("Gender change for {Name}: race {OldRace}→{NewRace}", session.Name, oldRace, race);

        await client.SendPacket(GenderChangePacketWriter.Change(session.CharacterId, race, face, hair));
        await zoneTransitionService.ChangeZoneAsync(session, session.ZoneId, session.X, session.Z);
    }

    private async Task PersistAsync(UserSession session)
    {
        using var scope = scopeFactory.CreateScope();
        var characters = scope.ServiceProvider.GetRequiredService<ICharacterRepository>();
        var character = await characters.GetById(session.CharacterId);
        if (character == null)
            return;
        character.Race = session.Race;
        character.Face = session.Face;
        character.Hair = session.Hair;
        await characters.UpdateAsync(character);
    }
}
