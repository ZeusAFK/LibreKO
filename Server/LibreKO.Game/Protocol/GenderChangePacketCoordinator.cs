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
    SessionManager sessionManager,
    IMagicItemUsageService magicItemUsageService,
    IZoneTransitionService zoneTransitionService,
    ICharacterStatePersister statePersister,
    IUserNotificationService userNotifications,
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

        var (oldRace, oldFace, oldHair) = (session.Race, session.Face, session.Hair);
        var journal = new InventoryJournal(session, logger);
        var outcome = await statePersister.CommitAsync(session, async db =>
        {
            var character = await db.Characters.FindAsync(session.CharacterId);
            if (character == null || !journal.Apply(s =>
                {
                    if (!journal.TakeOne(GenderChangeItem))
                        return false;
                    (oldRace, oldFace, oldHair) = (s.Race, s.Face, s.Hair);
                    (s.Race, s.Face, s.Hair) = (race, face, hair);
                    return true;
                }))
                return false;

            character.Race = race;
            return true;
        }, () => session.WithLock(s =>
        {
            journal.Restore();
            (s.Race, s.Face, s.Hair) = (oldRace, oldFace, oldHair);
        }));
        if (outcome != StateCommit.Saved)
        {
            await client.SendPacket(GenderChangePacketWriter.Result(
                outcome == StateCommit.Failed ? GenderChangePacketWriter.Failed : GenderChangePacketWriter.NoItem));
            return;
        }

        var stack = journal.TouchedSlots.Single();
        var item = session.Inventory[stack];
        await userNotifications.SendStackChangeAsync(session, (byte)stack, item.ItemId, item.Count, item.Durability);

        logger.LogInformation("Gender change for {Name}: race {OldRace}→{NewRace}", session.Name, oldRace, race);

        await client.SendPacket(GenderChangePacketWriter.Change(session.CharacterId, race, face, hair));
        await zoneTransitionService.ChangeZoneAsync(session, session.ZoneId, session.X, session.Z);
    }
}
