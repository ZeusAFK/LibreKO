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
    IGameDataService gameDataService,
    IZoneTransitionService zoneTransitionService,
    EventSchedulerService eventSchedulerService,
    ILoyaltyService loyaltyService,
    ILogger<EventSystemsPacketCoordinator> logger) : IEventSystemsPacketCoordinator
{
    private const byte TempleEventMonsterStone = 6;
    private const byte TempleEventJoin = 8;
    private const byte TempleEventDisband = 9;

    private const int ItemMonsterStone = 900144023;
    private static readonly byte[] MonsterStoneZones = [21, 22, 23, 24, 25];

    private const byte BattleEventOpen = 1;
    private const byte BattleMapEventResult = 2;
    private const byte BattleEventResult = 3;
    private const byte BattleEventMaxUser = 4;
    private const byte BattleEventKillUser = 5;

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
                await HandleMonsterStoneAsync(session);
                break;

            case TempleEventJoin:
                await HandleTempleEventJoinAsync(session);
                break;

            case TempleEventDisband:
                await HandleTempleEventDisbandAsync(session);
                break;
        }
    }

    public async Task HandleBattleEventAsync(IClient client, Packet packet)
    {
        var session = sessionManager.GetByClientId(client.Id);
        if (session == null || packet.RemainingBytes < 1)
            return;

        var subOpcode = packet.ReadByte();
        switch (subOpcode)
        {
            case BattleEventOpen:
                await HandleBattleEventOpenAsync(session);
                break;

            case BattleMapEventResult:
                HandleBattleMapEventResult(packet);
                break;

            case BattleEventResult:
                await HandleBattleEventResultAsync(session, packet);
                break;

            case BattleEventMaxUser:
                await HandleBattleEventMaxUserAsync(session, packet);
                break;

            case BattleEventKillUser:
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

        var eventType = packet.ReadByte();
        var battle = sessionManager.Battle;

        Packet response;
        if (battle.IsBattleActive && session.ZoneId == BattleZoneManager.ZONE_BATTLE4)
        {
            response = EventPacketWriter.MapEventScores(
                eventType, (short)battle.KarusMonumentPoint, (short)battle.ElmoMonumentPoint);
        }
        else if (battle.IsBattleActive && session.ZoneId == BattleZoneManager.ZONE_BATTLE6)
        {
            response = EventPacketWriter.MapEventScores(
                eventType, battle.KarusDead, battle.ElmoradDead);
        }
        else
        {
            response = EventPacketWriter.MapEventEmpty();
        }

        await session.Client.SendPacket(response);
    }

    public async Task OpenBattleZoneAsync(byte type, byte zone)
    {
        var battle = sessionManager.Battle;
        if (!battle.OpenBattleZone(type, zone))
            return;

        var packet = EventPacketWriter.BattleZoneOpened(
            BattleEventOpen,
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
        if (winner > 0)
            await AnnounceBattleResultAsync(winner);

        battle.CloseBattleZone();

        var packet = EventPacketWriter.BattleZoneClosed(
            BattleEventOpen, BattleZoneManager.BATTLEZONE_CLOSE);
        await sessionManager.BroadcastToAll(packet);
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

    private async Task HandleMonsterStoneAsync(UserSession session)
    {
        var slotIndex = -1;
        for (var index = InventoryConstants.SlotMax; index < InventoryConstants.SlotMax + InventoryConstants.HaveMax; index++)
        {
            if (session.Inventory[index].ItemId == ItemMonsterStone)
            {
                slotIndex = index;
                break;
            }
        }

        if (slotIndex < 0)
            return;

        var slot = session.Inventory[slotIndex];
        if (slot.Count > 1)
            slot.Count--;
        else
            slot.Clear();

        var countPacket = new ItemCountChangePacketWriter()
            .Add((byte)slotIndex, slot.ItemId, slot.Count, slot.Durability)
            .Build();
        await session.Client.SendPacket(countPacket);

        var targetZone = MonsterStoneZones[Random.Shared.Next(MonsterStoneZones.Length)];
        var startPos = gameDataService.GetStartPosition(targetZone);
        if (startPos == null)
            return;

        var x = startPos.BaseX(session.Nation) / 10.0f;
        var z = startPos.BaseZ(session.Nation) / 10.0f;
        await zoneTransitionService.ChangeZoneAsync(session, targetZone, x, z);
    }

    public Task JoinTempleEventAsync(UserSession session) => HandleTempleEventJoinAsync(session);

    private async Task HandleTempleEventJoinAsync(UserSession session)
    {
        Packet packet;

        if (eventSchedulerService.TryJoinTempleEvent(session))
        {
            packet = EventPacketWriter.TempleEvent(
                TempleEventJoin, 1, eventSchedulerService.TempleEventZone);
            logger.LogDebug("Player {Name} joined temple event zone {Zone}", session.Name, eventSchedulerService.TempleEventZone);
        }
        else
        {
            packet = EventPacketWriter.TempleEvent(TempleEventJoin, 0, 0);
        }

        await session.Client.SendPacket(packet);
    }

    private async Task HandleTempleEventDisbandAsync(UserSession session)
    {
        eventSchedulerService.LeaveTempleEvent(session.CharacterId);

        var packet = EventPacketWriter.TempleEvent(TempleEventDisband, 1, 0);
        await session.Client.SendPacket(packet);
    }

    private async Task HandleBattleEventOpenAsync(UserSession session)
    {
        var battle = sessionManager.Battle;

        var secondsRemaining = battle.IsBattleActive
            ? (int)Math.Max(0, (battle.BattleDuration - battle.GetElapsedTime()).TotalSeconds)
            : 0;

        var packet = EventPacketWriter.BattleZoneState(
            BattleEventOpen, battle.BattleOpen, battle.BattleZone, secondsRemaining);

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
        var winnerPacket = EventPacketWriter.BattleDeclare(
            BattleEventResult, BattleZoneManager.DECLARE_WINNER, winnerNation);
        await sessionManager.BroadcastToAll(winnerPacket);

        var loserPacket = EventPacketWriter.BattleDeclare(
            BattleEventResult, BattleZoneManager.DECLARE_LOSER,
            winnerNation == (byte)AccountNation.Karus
                ? (byte)AccountNation.ElMorad
                : (byte)AccountNation.Karus);
        await sessionManager.BroadcastToAll(loserPacket);
    }

}
