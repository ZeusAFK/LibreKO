using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;

using LibreKO.Game.Protocol.Writers;

namespace LibreKO.Game.Protocol;

public interface IEventSystemsPacketCoordinator
{
    Task HandleEventAsync(IClient client, Packet packet);
    Task HandleBattleEventAsync(IClient client, Packet packet);
    Task HandlePvpAsync(IClient client, Packet packet);
    Task HandleMapEventAsync(IClient client, Packet packet);
    Task OpenBattleZoneAsync(byte type, byte zone);
    Task CloseBattleZoneAsync();
    Task AssignRivalAsync(UserSession player, UserSession rival);
    Task RemoveRivalAsync(UserSession player);
    Task UpdateAngerGaugeAsync(UserSession player, byte gauge);
    Task CheckRivalExpiryAsync(UserSession player);
    Task JoinTempleEventAsync(UserSession session);
}

public class EventSystemsPacketCoordinator(
    SessionManager sessionManager,
    IZoneTransitionService zoneTransitionService,
    InstanceRoomRegistry instanceRooms,
    EventSchedulerService eventSchedulerService,
    ILoyaltyService loyaltyService,
    IMonsterStoneService monsterStoneService,
    IDrakiTowerService drakiTowerService,
    IFullMoonRiftService fullMoonRiftService,
    INereidMonumentService nereidMonuments,
    IPartyUserVoteService userVote,
    ILogger<EventSystemsPacketCoordinator> logger) : IEventSystemsPacketCoordinator
{
    private const byte TempleEventMonsterStone = 6;
    private const byte TempleEventMonsterStoneExp = 11;
    private const byte TempleEventJoin = 8;
    private const byte TempleEventDisband = 9;

    private const int WarderKillLoyalty = 500;
    private const int GatekeeperKillLoyalty = 1000;

    private const byte PvpAssignRival = 1;
    private const byte PvpRemoveRival = 2;
    private const byte PvpUpdateHelmet = 5;
    private const byte PvpResetHelmet = 6;

    private const int RivalryDurationSeconds = 300;
    private const byte MaxAngerGauge = 5;

    public async Task HandleEventAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || session.Hp <= 0 || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case TempleEventMonsterStone:
                if (packet.RemainingBytes >= sizeof(int))
                    await monsterStoneService.UseAsync(session, packet.ReadInt());
                break;

            case TempleEventMonsterStoneExp:
                if (packet.RemainingBytes >= sizeof(int))
                    await monsterStoneService.ExpAsync(session, packet.ReadInt());
                break;

            case TempleEventJoin:
                await HandleTempleEventJoinAsync(session, RequestedContest(packet));
                break;

            case TempleEventDisband:
                await HandleTempleEventDisbandAsync(session);
                break;

            case (byte)TempleSubOpcode.DrakiEnter:
                await drakiTowerService.HandleEnterAsync(session, packet);
                break;

            case (byte)TempleSubOpcode.DrakiList:
                await drakiTowerService.HandleListAsync(session);
                break;

            case (byte)TempleSubOpcode.DrakiTown:
                await drakiTowerService.HandleTownAsync(session);
                break;

            case FullMoonRules.EntrySub:
                await HandleFullMoonEntryAsync(session);
                break;
        }
    }

    private async Task HandleFullMoonEntryAsync(UserSession session)
    {
        await fullMoonRiftService.HandlePartyEntryAsync(session);
    }

    public async Task HandleBattleEventAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = (BattleEventSubOpcode)packet.ReadByte();
        switch (subOpcode)
        {
            case BattleEventSubOpcode.Open:
                await HandleBattleEventOpenAsync(session);
                break;

            case BattleEventSubOpcode.MapEventResult:
                HandleBattleMapEventResult(packet);
                break;

            case BattleEventSubOpcode.Result:
                await HandleBattleEventResultAsync(session, packet);
                break;

            case BattleEventSubOpcode.MaxUser:
                await HandleBattleEventMaxUserAsync(session, packet);
                break;

            case BattleEventSubOpcode.KillUser:
                break;

            default:
                logger.LogDebug("Unhandled battle event sub-opcode {Sub} from {Name}", subOpcode, session.Name);
                break;
        }
    }

    public Task HandlePvpAsync(IClient client, Packet packet)
    {
        // WIZ_PVP is server-to-client only (rival assign/remove/anger gauge).
        // The client should not send this opcode; silently ignore if it does.
        return Task.CompletedTask;
    }

    public async Task HandleMapEventAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        if (packet.ReadByte() != MapEventPacketWriter.SubPoints)
            return;

        var battle = sessionManager.Battle;
        if (session.ZoneId == BattleZoneManager.ZONE_BATTLE4)
            await session.Client.SendPacket(nereidMonuments.PointsPacket());
        else if (battle.IsBattleActive && session.ZoneId == BattleZoneManager.ZONE_BATTLE6)
            await session.Client.SendPacket(MapEventPacketWriter.Points((ushort)battle.KarusDead, (ushort)battle.ElmoradDead));
    }

    public async Task OpenBattleZoneAsync(byte type, byte zone)
    {
        var battle = sessionManager.Battle;
        if (!battle.OpenBattleZone(type, zone))
            return;

        var packet = BattleEventPacketWriter.ZoneOpened(
            type == BattleZoneManager.SNOW_BATTLEZONE_OPEN
                ? BattleZoneManager.SNOW_BATTLEZONE_OPEN
                : BattleZoneManager.BATTLEZONE_OPEN,
            zone);
        await sessionManager.BroadcastToAll(packet);
    }

    public async Task CloseBattleZoneAsync()
    {
        var battle = sessionManager.Battle;
        if (!battle.IsBattleActive)
            return;

        var winner = battle.DetermineWinner();
        await nereidMonuments.AnnounceResultAsync(winner);
        if (winner > 0)
            await AnnounceBattleResultAsync(winner);

        var zone = battle.BattleZone;
        battle.CloseBattleZone();
        await sessionManager.BroadcastToAll(BattleEventPacketWriter.ZoneClosed(zone));
    }

    public async Task AssignRivalAsync(UserSession player, UserSession rival)
    {
        if (player.HasRival)
            return;

        player.RivalId = rival.CharacterId;
        player.RivalExpiryTime = DateTime.UtcNow.AddSeconds(RivalryDurationSeconds);

        var clanName = sessionManager.Knights.GetClan(rival.KnightsId)?.Name ?? string.Empty;
        await player.Client.SendPacket(EventPacketWriter.AssignRival(
            PvpAssignRival, rival.CharacterId, player.Money, player.Loyalty,
            clanName, rival.Name ?? string.Empty));
    }

    public async Task RemoveRivalAsync(UserSession player)
    {
        if (!player.HasRival)
            return;

        player.RivalId = -1;
        player.RivalExpiryTime = default;

        await player.Client.SendPacket(EventPacketWriter.PvpFlag(PvpRemoveRival));
    }

    public async Task UpdateAngerGaugeAsync(UserSession player, byte gauge)
    {
        player.AngerGauge = Math.Min(gauge, MaxAngerGauge);

        Packet packet;
        if (gauge == 0)
        {
            packet = EventPacketWriter.PvpFlag(PvpResetHelmet);
        }
        else
        {
            packet = EventPacketWriter.AngerGauge(
                PvpUpdateHelmet, player.AngerGauge,
                player.HasFullAngerGauge ? (byte)1 : (byte)0);
        }

        await player.Client.SendPacket(packet);
    }

    public async Task CheckRivalExpiryAsync(UserSession player)
    {
        if (!player.HasRival || DateTime.UtcNow < player.RivalExpiryTime)
            return;

        await RemoveRivalAsync(player);
    }

    public Task JoinTempleEventAsync(UserSession session) => HandleTempleEventJoinAsync(session, TempleEvent.None);

    private static TempleEvent RequestedContest(Packet packet)
    {
        if (packet.RemainingBytes < 1)
            return TempleEvent.None;
        var contest = (TempleEvent)packet.ReadByte();
        return Enum.IsDefined(contest) ? contest : TempleEvent.None;
    }

    private async Task HandleTempleEventJoinAsync(UserSession session, TempleEvent requested)
    {
        var contest = requested != TempleEvent.None ? requested : eventSchedulerService.SignUpFor(session);
        if (contest == TempleEvent.JuraidMountain
            && await userVote.JuraidBarAsync(session.CharacterId) is { } bar)
        {
            await session.Client.SendPacket(InstanceDashboardPacketWriter.JuraidWarning(bar.Warnings, bar.Limit));
            return;
        }
        var result = eventSchedulerService.TryJoinTempleEvent(session, contest);
        await session.Client.SendPacket(EventPacketWriter.TempleEvent(
            TempleEventJoin, (byte)result,
            TempleEventRules.JoinReplyId(contest, TempleEventRules.ZoneFor(contest))));
        if (result != TempleJoinResult.Joined)
            return;

        logger.LogDebug("Player {Name} joined {Contest}", session.Name, contest);
        await BroadcastTempleSignUpsAsync(contest);
    }

    private async Task BroadcastTempleSignUpsAsync(TempleEvent contest)
    {
        var (karus, elMorad) = eventSchedulerService.TempleSignUps(contest);
        await sessionManager.BroadcastToAll(EventPacketWriter.TempleSignUps(contest, karus, elMorad));
    }

    private async Task HandleTempleEventDisbandAsync(UserSession session)
    {
        var leftSignUp = eventSchedulerService.LeaveTempleEvent(session.CharacterId);

        if (session.InstanceReturn is { } returnPoint)
        {
            logger.LogInformation("Player {Name} left instance room {Room}. Warping to return point ({Zone}, {X}, {Z})",
                session.Name, session.Room, returnPoint.ZoneId, returnPoint.X, returnPoint.Z);
            instanceRooms.Leave(session);
            await zoneTransitionService.ChangeZoneAsync(session, returnPoint.ZoneId, returnPoint.X, returnPoint.Z);
        }
        else if (CharacterReconnectZoneRepair.IsEventZone(session.ZoneId))
        {
            logger.LogInformation("Player {Name} left event from zone {Zone}. Warping to Moradon",
                session.Name, session.ZoneId);
            await zoneTransitionService.ChangeZoneAsync(session, (byte)ZoneId.Moradon, 0f, 0f);
        }

        var packet = EventPacketWriter.TempleEvent(TempleEventDisband, 1, 0);
        await session.Client.SendPacket(packet);
        if (leftSignUp != TempleEvent.None)
            await BroadcastTempleSignUpsAsync(leftSignUp);
    }

    private async Task HandleBattleEventOpenAsync(UserSession session)
    {
        var battle = sessionManager.Battle;

        var secondsRemaining = battle.IsBattleActive
            ? (int)Math.Max(0, (battle.BattleDuration - battle.GetElapsedTime()).TotalSeconds)
            : 0;

        var packet = BattleEventPacketWriter.ZoneState(battle.BattleOpen, battle.BattleZone, secondsRemaining);

        await session.Client.SendPacket(packet);
    }

    private void HandleBattleMapEventResult(Packet packet)
    {
        var battle = sessionManager.Battle;
        if (!battle.IsBattleActive || packet.RemainingBytes < 1)
            return;

        var nation = packet.ReadByte();
        if (nation == 1)
            battle.KarusOpenFlag = true;
        else if (nation == 2)
            battle.ElmoradOpenFlag = true;
    }

    private async Task HandleBattleEventResultAsync(UserSession session, Packet packet)
    {
        var battle = sessionManager.Battle;
        if (!battle.IsBattleActive || packet.RemainingBytes < 1)
            return;

        packet.ReadByte();
        var winnerNation = battle.RegisterNpcKill((byte)session.Nation);
        if (winnerNation > 0)
        {
            battle.Victory = winnerNation;
            await AnnounceBattleResultAsync(winnerNation);
        }

        battle.BattleSaved = true;
    }

    private async Task HandleBattleEventMaxUserAsync(UserSession session, Packet packet)
    {
        var battle = sessionManager.Battle;
        if (!battle.IsBattleActive || packet.RemainingBytes < 1)
            return;

        var npcType = packet.ReadByte();
        var loyaltyReward = npcType switch
        {
            >= 3 and <= 6 => WarderKillLoyalty,
            7 or 8 => GatekeeperKillLoyalty,
            _ => 0
        };

        if (loyaltyReward > 0)
            await loyaltyService.ChangeAsync(session, loyaltyReward);
    }

    private async Task AnnounceBattleResultAsync(byte winnerNation)
    {
        foreach (var packet in BattleEventPacketWriter.Winner(winnerNation))
            await sessionManager.BroadcastToAll(packet);
    }

}
