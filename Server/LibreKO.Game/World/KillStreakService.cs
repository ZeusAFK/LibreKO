using System.Collections.Concurrent;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IKillStreakService
{
    Task OnPlayerKilledAsync(UserSession victim, UserSession killer);
    Task OnPlayerDiedAsync(UserSession victim);
    void Forget(UserSession session);
    int StreakOf(int characterId);
}

public sealed class KillStreakService(
    SessionManager sessionManager,
    ILogger<KillStreakService> logger) : IKillStreakService
{
    public const int AnnouncementStep = 10;

    private readonly ConcurrentDictionary<int, int> streaks = new();

    public int StreakOf(int characterId) => streaks.GetValueOrDefault(characterId);

    public void Forget(UserSession session) => streaks.TryRemove(session.CharacterId, out _);

    public async Task OnPlayerKilledAsync(UserSession victim, UserSession killer)
    {
        int streak = streaks.AddOrUpdate(killer.CharacterId, 1, (_, current) => current + 1);
        ushort count = (ushort)Math.Min(streak, ushort.MaxValue);
        await SendAsync(killer, KillAssistPacketWriter.Kill(
            KillAssistPacketWriter.OwnStreak, killer.CharacterId, killer.Name, (byte)killer.Nation, count));

        if (streak % AnnouncementStep == 0)
        {
            var announcement = KillAssistPacketWriter.Kill(
                KillAssistPacketWriter.StreakAnnouncement, killer.CharacterId, killer.Name, (byte)killer.Nation, count);
            foreach (var other in sessionManager.GetAll())
                if (other.ZoneId == killer.ZoneId)
                    await SendAsync(other, announcement);
        }
    }

    public async Task OnPlayerDiedAsync(UserSession victim)
    {
        if (streaks.TryRemove(victim.CharacterId, out int lost) && lost > 0)
            await SendAsync(victim, KillAssistPacketWriter.Kill(
                KillAssistPacketWriter.OwnStreak, victim.CharacterId, victim.Name, (byte)victim.Nation, 0));
    }

    private async Task SendAsync(UserSession session, Common.Infrastructure.Network.Packet packet)
    {
        try { await session.Client.SendPacket(packet); }
        catch (Exception ex) { logger.LogDebug(ex, "Kill streak send failed for {Id}", session.CharacterId); }
    }
}
