using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface ICombatLifecycleService
{
    void SetNpcAggro(NpcInstance npc, UserSession attacker);
    Task HandleRegeneAsync(IClient client, UserSession session, byte _regeneType);
    Task HandleNpcDeathAsync(NpcInstance npc, UserSession killer);
    Task HandlePlayerDeathAsync(UserSession victim, UserSession? killer);
    Task SendHpChangeAsync(UserSession session, int attackerId = -1);
    Task SendMspChangeAsync(UserSession session);
    Task SendNpcTargetHpAsync(UserSession attacker, NpcInstance npc, int damage = 0);
    Task SendPlayerTargetHpAsync(UserSession attacker, UserSession victim, int damage = 0);
}

public class CombatLifecycleService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IPlayerProgressionService playerProgressionService,
    ICombatNotificationService combatNotificationService,
    ILoyaltyService loyaltyService,
    ICombatRewardService combatRewardService,
    IExchangePacketCoordinator exchangePacketCoordinator,
    IMiningPacketCoordinator miningPacketCoordinator,
    IEventSystemsPacketCoordinator eventSystemsPacketCoordinator,
    IWorldVisibilityService worldVisibilityService,
    IZoneTransitionService zoneTransitionService,
    ISavedMagicService savedMagicService,
    IStealthService stealthService,
    ILogger<CombatLifecycleService> logger) : ICombatLifecycleService
{
    public const int NoKillerId = -1;

    public void SetNpcAggro(NpcInstance npc, UserSession attacker)
    {
        if (npc.IsScarecrow)
            return;

        var currentTarget = npc.TargetUserId > 0
            ? sessionManager.GetByCharacterId(npc.TargetUserId)
            : null;
        if (currentTarget != null && currentTarget.Hp > 0 && currentTarget.ZoneId == npc.ZoneId)
        {
            logger.LogDebug(
                "NPC aggro unchanged: npc={NpcId}/{UniqueId} attacker={AttackerId}/{AttackerName} currentTarget={CurrentTargetId}/{CurrentTargetName} state={State}",
                npc.NpcId,
                npc.UniqueId,
                attacker.CharacterId,
                attacker.Name,
                currentTarget.CharacterId,
                currentTarget.Name,
                npc.State);
            return;
        }

        var previousTargetId = npc.TargetUserId;
        var previousState = npc.State;
        npc.EngageTarget(attacker, allowStateInterrupt: true);
        sessionManager.Regions.MarkNpcEngaged(npc);
        logger.LogDebug(
            "NPC aggro engaged: npc={NpcId}/{UniqueId} attacker={AttackerId}/{AttackerName} previousTarget={PreviousTargetId} previousState={PreviousState} newTarget={NewTargetId} newState={NewState}",
            npc.NpcId,
            npc.UniqueId,
            attacker.CharacterId,
            attacker.Name,
            previousTargetId,
            previousState,
            npc.TargetUserId,
            npc.State);
    }

    public async Task HandleRegeneAsync(IClient client, UserSession session, byte _regeneType)
    {
        if (session.Hp > 0)
        {
            logger.LogWarning(
                "Regene requested by a live session {CharacterId}/{Name} (hp={Hp}) — re-syncing HP so the client leaves its dead state",
                session.CharacterId, session.Name, session.Hp);
            await SendHpChangeAsync(session);
            return;
        }

        var inArena = ArenaZones.TryGetExit(session.ArenaId, out var exitX, out var exitZ);

        var startPos = gameDataService.GetStartPosition(session.ZoneId);
        if (startPos == null && !inArena)
        {
            logger.LogWarning(
                "Zone {ZoneId} has no start position — respawning {CharacterId}/{Name} in place rather than leaving them dead",
                session.ZoneId, session.CharacterId, session.Name);
            await ReviveInPlaceAsync(client, session);
            return;
        }

        session.KillerNpcType = 0;
        session.DeathExpLoss = 0;
        sessionManager.Regions.DropAggroOn(session.CharacterId);

        await worldVisibilityService.BroadcastUserInOutAsync(session, InOutType.Out);

        var rand = Random.Shared;
        float x;
        float z;

        var bindEvent = session.Quest.BindPoint > 0
            ? sessionManager.Maps?.GetObjectEvent(session.ZoneId, session.Quest.BindPoint)
            : null;
        if (inArena)
        {
            x = exitX + rand.Next(-2, 3);
            z = exitZ + rand.Next(-2, 3);
        }
        else if (bindEvent != null && bindEvent.Life == 1)
        {
            x = bindEvent.PosX + rand.Next(-5, 6);
            z = bindEvent.PosZ + rand.Next(-5, 6);
        }
        else
        {
            (x, z) = startPos!.RandomSpawn(session.Nation);
        }

        session.X = x;
        session.Z = z;

        await SendRespawnAsync(client, session);

        sessionManager.Regions.UpdateRegion(session);
        await zoneTransitionService.RefreshArenaAsync(session);

        await worldVisibilityService.BroadcastUserInOutAsync(session, InOutType.Warp);
        await worldVisibilityService.SendNearbyUsersToClientAsync(session);
    }

    private async Task ReviveInPlaceAsync(IClient client, UserSession session)
    {
        session.KillerNpcType = 0;
        session.DeathExpLoss = 0;
        await SendRespawnAsync(client, session);
    }

    private async Task SendRespawnAsync(IClient client, UserSession session)
    {
        var result = RegenePacketWriter.At(session.GetPosX, session.GetPosZ);
        await client.SendPacket(result);

        await savedMagicService.DropVolatileAsync(session);
        await stealthService.ClearSightAsync(session);

        session.Hp = session.MaxHp;
        await SendHpChangeAsync(session);
    }

    public async Task HandleNpcDeathAsync(NpcInstance npc, UserSession killer)
    {
        if (npc.State == NpcState.Sleeping)
            await sessionManager.Regions.BroadcastFromNpc(
                npc,
                MovementPacketWriter.StateChange(
                    npc.UniqueId, (byte)StateChangeType.Pose, (byte)NpcPoseState.Awake));

        npc.DeathTimeTicks = DateTime.UtcNow.Ticks;
        npc.State = NpcState.Dead;
        npc.TargetUserId = 0;
        npc.IsMoving = false;
        npc.WakeTicks = 0;
        sessionManager.Regions.MarkNpcIdle(npc);

        var deadPacket = DeathPacketWriter.NpcDeath(npc.UniqueId);
        await sessionManager.Regions.BroadcastFromNpc(npc, deadPacket);

        var rewardRecipient = killer;
        if (npc.TopDamagerCharId > 0)
        {
            var topDamager = sessionManager.GetByCharacterId(npc.TopDamagerCharId);
            if (topDamager != null && topDamager.Hp > 0 && topDamager.ZoneId == npc.ZoneId)
                rewardRecipient = topDamager;
        }

        // Record the last killed NPC for the recipient; quest scripts read it.
        rewardRecipient.LastKilledNpcId = npc.NpcId;

        await combatRewardService.AwardNpcKillAsync(npc, rewardRecipient);
    }

    public async Task HandlePlayerDeathAsync(UserSession victim, UserSession? killer)
    {
        if (killer?.CharacterId == victim.CharacterId)
            killer = null;

        if (victim.Trade.IsTrading)
            await exchangePacketCoordinator.CancelAsync(victim, isOnDeath: true);

        await miningPacketCoordinator.StopGatheringAsync(victim);

        victim.Deaths++;
        victim.DeathExpLoss = 0;

        var deadPacket = DeathPacketWriter.PlayerDeath(
            victim.CharacterId, killer?.CharacterId ?? NoKillerId);
        await sessionManager.Regions.SendToRegion(victim, deadPacket, excludeSender: false);

        await combatRewardService.AwardPlayerKillAsync(victim, killer);

        if (killer == null)
        {
            victim.KillerNpcType = 0;
            return;
        }

        var penaltyFree = PvpRules.IsPenaltyFreeDeath(victim, killer);
        var avenged = killer.HasRival && killer.RivalId == victim.CharacterId;

        if (killer.Nation != victim.Nation && !penaltyFree)
        {
            await combatNotificationService.SendDeathNoticeAsync(killer, victim);

            if (victim.Loyalty <= 0)
            {
                var noPointsLoss = DeathPenaltyCalculator.CalculatePlayerDeathExpLoss(victim, gameDataService);
                victim.DeathExpLoss = noPointsLoss;
                if (noPointsLoss > 0)
                    await playerProgressionService.ChangeExperienceAsync(victim, -noPointsLoss);
            }
            else
            {
                var party = killer.IsInParty
                    ? sessionManager.Parties.GetParty(killer.PartyIndex)
                    : null;
                var award = LoyaltyAwards.PerPartyMember(
                    LoyaltyAwards.For(victim.ZoneId), party?.MemberCount ?? 1);

                await loyaltyService.ChangeAsync(
                    victim, LoyaltyAwards.WithRivalryBonus(award, avenged).Victim);
                if (party == null)
                {
                    await loyaltyService.ChangeAsync(
                        killer, LoyaltyAwards.WithRivalryBonus(award, avenged).Killer);
                }
                else
                {
                    foreach (var memberId in party.MemberIds)
                    {
                        if (memberId <= 0)
                            continue;
                        var member = sessionManager.GetByCharacterId(memberId);
                        if (member == null || member.Hp <= 0)
                            continue;

                        var avengedByMember = member.HasRival
                            && member.RivalId == victim.CharacterId;
                        await loyaltyService.ChangeAsync(
                            member,
                            LoyaltyAwards.WithRivalryBonus(award, avengedByMember).Killer);
                        if (avengedByMember)
                            member.RivalId = UserSession.NoRival;
                    }
                }
            }
        }

        if (penaltyFree)
        {
            victim.KillerNpcType = 0;
            return;
        }

        var battle = sessionManager.Battle;
        if (battle.IsBattleActive && BattleZoneManager.IsBattleZone(victim.ZoneId))
            battle.RegisterDeath((byte)victim.Nation);

        if (BattleZoneManager.IsPkZone(victim.ZoneId))
        {
            if (avenged && !killer.IsInParty)
                await eventSystemsPacketCoordinator.RemoveRivalAsync(killer);

            if (!victim.HasFullAngerGauge)
                await eventSystemsPacketCoordinator.UpdateAngerGaugeAsync(
                    victim, (byte)(victim.AngerGauge + 1));

            if (!victim.HasRival)
                await eventSystemsPacketCoordinator.AssignRivalAsync(victim, killer);
        }

        await eventSystemsPacketCoordinator.CheckRivalExpiryAsync(killer);
        await eventSystemsPacketCoordinator.CheckRivalExpiryAsync(victim);

        victim.KillerNpcType = 0;
    }

    public Task SendHpChangeAsync(UserSession session, int attackerId = -1) =>
        combatNotificationService.SendHpChangeAsync(session, attackerId);

    public Task SendMspChangeAsync(UserSession session) =>
        combatNotificationService.SendMspChangeAsync(session);

    public Task SendNpcTargetHpAsync(UserSession attacker, NpcInstance npc, int damage = 0) =>
        combatNotificationService.SendNpcTargetHpAsync(attacker, npc, damage);

    public Task SendPlayerTargetHpAsync(UserSession attacker, UserSession victim, int damage = 0) =>
        combatNotificationService.SendPlayerTargetHpAsync(attacker, victim, damage);

}
