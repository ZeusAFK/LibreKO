using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface ISavedMagicService
{
    Task DropVolatileAsync(UserSession session);
    Task RecastAsync(UserSession session);
}

public class SavedMagicService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IUserNotificationService userNotificationService) : ISavedMagicService
{
    public async Task DropVolatileAsync(UserSession session)
    {
        if (session.DropVolatileMagic(gameDataService) > 0)
            await userNotificationService.SendStatUpdateAsync(session);

        await RecastAsync(session);
    }

    public async Task RecastAsync(UserSession session)
    {
        foreach (var (magicId, buff) in UserSessionMagicState.LiveStatusBuffs(session, gameDataService))
        {
            var remaining = (short)Math.Clamp(
                (buff.ExpireTicks - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerSecond,
                0,
                short.MaxValue);
            if (remaining <= 0)
                continue;

            if (gameDataService.GetMagic(magicId)?.PrimaryType == MagicSkillType.Transform)
            {
                await sessionManager.Regions.SendToRegion(
                    session,
                    MovementPacketWriter.Transformation(
                        session.CharacterId, (byte)StateChangeType.Transformation, magicId),
                    excludeSender: false);
            }

            await session.Client.SendPacket(MagicProcessPacketWriter.Create(
                MagicProcessOpcode.Effecting,
                magicId,
                session.CharacterId,
                session.CharacterId,
                [0, 1, 0, remaining, 0, buff.BonusSpeed, 0]));
        }
    }
}
