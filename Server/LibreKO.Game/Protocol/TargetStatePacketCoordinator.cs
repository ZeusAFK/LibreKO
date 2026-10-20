using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.World;

namespace LibreKO.Game.Protocol;

public interface ITargetStatePacketCoordinator
{
    Task HandleAsync(IClient client, Packet packet);
}

public sealed class TargetStatePacketCoordinator(SessionManager sessionManager, IGameDataService gameDataService)
    : ITargetStatePacketCoordinator
{
    public async Task HandleAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 5)
            return;

        var sub = packet.ReadByte();
        var targetId = packet.ReadInt();
        switch (sub)
        {
            case TargetStatePacketWriter.SubEffects:
                if (EffectsOf(session, targetId) is { } effects)
                    await client.SendPacket(TargetStatePacketWriter.Effects(targetId, effects));
                break;
            case TargetStatePacketWriter.SubDropList:
                await client.SendPacket(TargetStatePacketWriter.DropList(targetId, DropListOf(session, targetId)));
                break;
        }
    }

    private DropSummary? DropListOf(UserSession viewer, int targetId) =>
        sessionManager.Regions.GetNpc(targetId) is { IsMonster: true } npc && npc.ZoneId == viewer.ZoneId
            ? DropSummaries.For(npc, gameDataService)
            : null;

    private List<TargetEffect>? EffectsOf(UserSession viewer, int targetId)
    {
        var now = DateTime.UtcNow.Ticks;
        if (sessionManager.GetByCharacterId(targetId) is { } player)
        {
            if (!Sight.Sees(viewer, player))
                return null;

            var effects = player.ActiveBuffs.Values
                .Where(buff => !buff.IsExpired)
                .Select(buff => new TargetEffect(buff.MagicId, Seconds(buff.ExpireTicks - now)))
                .ToList();
            foreach (var overTime in player.ActiveOverTimeEffects.Values)
            {
                var ticksLeft = Math.Max(0, overTime.TickLimit - overTime.TickCount);
                effects.Add(new TargetEffect(overTime.MagicId, (ushort)Math.Min(ushort.MaxValue, ticksLeft * overTime.TickIntervalSeconds)));
            }
            return effects;
        }

        if (sessionManager.Regions.GetNpc(targetId) is { } npc && npc.ZoneId == viewer.ZoneId)
            return npc.Debuffs.Active(now)
                .Select(debuff => new TargetEffect(debuff.SkillId, Seconds(debuff.ExpireTicks - now)))
                .ToList();

        return null;
    }

    private static ushort Seconds(long ticks) =>
        (ushort)Math.Clamp((ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond, 0, ushort.MaxValue);
}
