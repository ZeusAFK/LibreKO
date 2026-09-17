using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface IStealthService
{
    Task HideAsync(UserSession session, InvisibilityType invisibility);
    Task RevealAsync(UserSession session, InvisibilityType dispelledBy);
    Task GrantSightAsync(UserSession session, short radius);
    Task ClearSightAsync(UserSession session);
    Task EndAsync(UserSession session, MagicStealthType stealthType);
}

public class StealthService(
    SessionManager sessionManager,
    IGameDataService gameDataService) : IStealthService
{
    public Task HideAsync(UserSession session, InvisibilityType invisibility)
    {
        session.Invisibility = invisibility;
        return BroadcastVisibilityAsync(session);
    }

    public async Task RevealAsync(UserSession session, InvisibilityType dispelledBy)
    {
        if (session.Invisibility == InvisibilityType.None)
            return;

        if (dispelledBy != InvisibilityType.None && session.Invisibility != dispelledBy)
            return;

        foreach (var skillId in ActiveStealthSkills(session))
            session.ActiveBuffs.TryRemove(skillId, out _);

        session.Invisibility = InvisibilityType.None;
        await BroadcastVisibilityAsync(session);
        await session.Client.SendPacket(
            MagicProcessPacketWriter.CreateDurationExpired(DurationExpiredCode.Stealth));
    }

    public Task GrantSightAsync(UserSession session, short radius) =>
        session.Client.SendPacket(StealthPacketWriter.Sight(radius));

    public Task ClearSightAsync(UserSession session) =>
        session.Client.SendPacket(StealthPacketWriter.NoSight());

    public async Task EndAsync(UserSession session, MagicStealthType stealthType)
    {
        switch (stealthType)
        {
            case MagicStealthType.DispelOnMove:
            case MagicStealthType.DispelOnAttack:
                await BroadcastVisibilityAsync(session);
                await session.Client.SendPacket(
                    MagicProcessPacketWriter.CreateDurationExpired(DurationExpiredCode.Stealth));
                break;

            case MagicStealthType.SeeInvisible:
            case MagicStealthType.SeeInvisibleParty:
                await ClearSightAsync(session);
                await session.Client.SendPacket(
                    MagicProcessPacketWriter.CreateDurationExpired(DurationExpiredCode.Sight));
                break;
        }
    }

    private Task BroadcastVisibilityAsync(UserSession session) =>
        sessionManager.Regions.SendToRegion(
            session,
            MovementPacketWriter.StateChange(
                session.CharacterId, (byte)StateChangeType.Stealth, (byte)session.Invisibility),
            excludeSender: false);

    private List<int> ActiveStealthSkills(UserSession session) =>
        session.ActiveBuffs.Keys
            .Where(skillId => StealthTypeOf(skillId)
                is MagicStealthType.DispelOnMove or MagicStealthType.DispelOnAttack)
            .ToList();

    private MagicStealthType StealthTypeOf(int skillId)
    {
        var magic = gameDataService.GetMagic(skillId);
        if (magic?.PrimaryType != MagicSkillType.Stealth
            || !MagicTypeLookup.TryResolve(gameDataService.MagicType9Table, magic, skillId, out var type9Data))
            return MagicStealthType.None;

        return (MagicStealthType)type9Data.StateChange;
    }
}
