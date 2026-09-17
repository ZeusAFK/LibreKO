using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.World;

public class HpMpRegenService(
    SessionManager sessionManager,
    ICombatNotificationService combatNotificationService,
    ILogger<HpMpRegenService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HP/MP regen service started ({Interval}s interval)",
            VitalsRegenCalculator.IntervalSeconds);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(VitalsRegenCalculator.IntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var session in sessionManager.GetAll())
                    await RegenerateAsync(session, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error in HP/MP regen tick");
            }
        }
    }

    private async Task RegenerateAsync(UserSession session, CancellationToken stoppingToken)
    {
        if (session.Hp <= 0 || session.IsWarping)
            return;

        var battle = sessionManager.Battle;
        var amounts = VitalsRegenCalculator.Calculate(new RegenSubject(
            session.Level,
            session.Hp,
            session.MaxHp,
            session.Mp,
            session.MaxMp,
            session.Class,
            session.ZoneId,
            session.IsSitting,
            session.IsGM,
            battle.BattleOpen == BattleZoneManager.SNOW_BATTLE));

        bool hpChanged = amounts.Hp > 0 && session.Hp < session.MaxHp;
        if (hpChanged)
            session.Hp = (short)Math.Min(session.MaxHp, session.Hp + amounts.Hp);

        bool mpChanged = amounts.Mp > 0 && session.Mp < session.MaxMp;
        if (mpChanged)
            session.Mp = (short)Math.Min(session.MaxMp, session.Mp + amounts.Mp);

        if (hpChanged)
            await session.Client.SendPacket(
                VitalsPacketWriter.HpChange(session.MaxHp, session.Hp, VitalsPacketWriter.NoAttacker),
                stoppingToken);

        if (mpChanged)
            await session.Client.SendPacket(
                VitalsPacketWriter.MpChange(session.MaxMp, session.Mp), stoppingToken);

        if ((hpChanged || mpChanged) && session.IsInParty)
            await combatNotificationService.SendPartyHpUpdateAsync(session);
    }
}
