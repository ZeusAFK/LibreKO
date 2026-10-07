using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public interface IUnderTheCastleService
{
    bool IsActive { get; }
    long RemainingSeconds { get; }
    int CurrentStage { get; }

    void Start(int? durationMinutes = null);
    Task CloseAsync();
    Task EnterAsync(UserSession session);
    Task OnNpcKilledAsync(NpcInstance npc, UserSession killer);
    Task TickAsync();
}

public sealed class UnderTheCastleService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IZoneTransitionService zoneTransitionService,
    INpcSpawnRowService npcSpawnRowService,
    INpcLifecycleService npcLifecycleService,
    IItemGrantService itemGrantService,
    ILogger<UnderTheCastleService> logger) : IUnderTheCastleService
{
    public const byte UtcZoneId = (byte)ZoneId.UnderCastle;
    public const int TrophyOfFlameItemId = 800149000;
    public const int TwinklingStarGlitterItemId = 810977000;
    public const int DentedIronmassItemId = 508147000;
    public const int IronPowderOfChainItemId = 508151000;
    public const int PlwitoonsTearItemId = 508152000;
    public const int HornOfPluwitonItemId = 810479000;
    public const int VictoryDelaySeconds = 60;

    public const int EmperorMammothNpcId = 9501;
    public const int CrasherGimmickNpcId = 9507;
    public const int ShackledLordFluwitonNpcId = 9566;
    public const int ShackledLordFluwitonAltNpcId = 9512;
    public const int PluwitonFinalBossNpcId = 9515;

    public const int Gate1DoorNpcId = 9550;
    public const int Gate2DoorNpcId = 9561;
    public const int Gate3DoorNpcId = 9562;
    public const int VictoryNpcId = 29197;

    private const float UtcCampX = 69f;
    private const float UtcCampZ = 64f;

    private sealed record StageReward(float AreaX, float AreaZ, float AreaRadius, int[] Items);

    public const float BossRewardRadius = 15f;

    private static readonly StageReward[] StageRewards =
    [
        new(0f, 0f, 0f, []),
        new(121f, 297f, 80f, [TwinklingStarGlitterItemId]),
        new(520f, 494f, 115f, [DentedIronmassItemId, TwinklingStarGlitterItemId]),
        new(642f, 351f, 100f, [IronPowderOfChainItemId, TwinklingStarGlitterItemId]),
        new(803f, 839f, 110f, [PlwitoonsTearItemId, HornOfPluwitonItemId, TwinklingStarGlitterItemId]),
    ];

    private readonly object _stateLock = new();
    private bool _isActive;
    private long _remainingSeconds;
    private int _currentStage = 1;
    private bool _finalBossKilled;
    private DateTime? _finishedAtUtc;
    private bool _returnNoticeSent;

    public bool IsActive
    {
        get { lock (_stateLock) return _isActive; }
    }

    public long RemainingSeconds
    {
        get { lock (_stateLock) return _remainingSeconds; }
    }

    public int CurrentStage
    {
        get { lock (_stateLock) return _currentStage; }
    }

    public void Start(int? durationMinutes = null)
    {
        lock (_stateLock)
        {
            if (_isActive)
            {
                logger.LogWarning("Under the Castle start requested but event is already active");
                return;
            }

            var minutes = durationMinutes ?? (TempleEventRules.UnderTheCastleDurationSeconds / 60);
            _remainingSeconds = minutes * 60;
            _isActive = true;
            _currentStage = 1;
            _finalBossKilled = false;
            _finishedAtUtc = null;
            _returnNoticeSent = false;

            logger.LogInformation("Under the Castle event started for {Minutes} minutes", minutes);
        }

        SpawnUtcMonsters();
        _ = sessionManager.BroadcastToAll(NoticePacketWriter.Broadcast("### [Under The Castle] Under the Castle has opened! You may now enter Under The Castle. ###"));
    }

    public async Task CloseAsync()
    {
        bool wasActive;
        lock (_stateLock)
        {
            wasActive = _isActive;
            _isActive = false;
            _remainingSeconds = 0;
            _finishedAtUtc = null;
        }

        if (!wasActive)
            return;

        logger.LogInformation("Under the Castle event closed");

        await KickOutZoneUsersAsync();
        DespawnUtcMonsters();
    }

    public async Task EnterAsync(UserSession session)
    {
        if (!IsActive && !session.IsGM)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, "Under the Castle is currently closed."));
            return;
        }

        if (session.Level < TempleEventRules.UnderTheCastleDefaultMinLevel && !session.IsGM)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, $"You must be at least level {TempleEventRules.UnderTheCastleDefaultMinLevel} to enter Under The Castle."));
            return;
        }

        await zoneTransitionService.ChangeZoneAsync(session, UtcZoneId, UtcCampX, UtcCampZ);
        await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
            (byte)session.Nation, "### Welcome to Under The Castle! Cooperate with your nation to defeat the horrors within! ###"));
    }

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession killer)
    {
        if (npc.ZoneId != UtcZoneId || !IsActive)
            return;

        int stageToUnlock = 0;
        string? noticeMessage = null;
        bool victory = false;

        lock (_stateLock)
        {
            if (_finalBossKilled)
                return;

            if (npc.NpcId == EmperorMammothNpcId && _currentStage == 1)
            {
                _currentStage = 2;
                stageToUnlock = 1;
                noticeMessage = $"### [Under The Castle] {npc.Name} defeated! Gate 1 is now OPEN! ###";
            }
            else if (npc.NpcId == CrasherGimmickNpcId && _currentStage == 2)
            {
                _currentStage = 3;
                stageToUnlock = 2;
                noticeMessage = $"### [Under The Castle] {npc.Name} defeated! Gate 2 is now OPEN! ###";
            }
            else if ((npc.NpcId == ShackledLordFluwitonNpcId || npc.NpcId == ShackledLordFluwitonAltNpcId) && _currentStage == 3)
            {
                _currentStage = 4;
                stageToUnlock = 3;
                noticeMessage = $"### [Under The Castle] {npc.Name} defeated! Final Chamber Gate is now OPEN! ###";
            }
            else if (npc.NpcId == PluwitonFinalBossNpcId && _currentStage == 4)
            {
                _currentStage = 5;
                _finalBossKilled = true;
                _finishedAtUtc = DateTime.UtcNow;
                victory = true;
                noticeMessage = $"### [Under The Castle] {npc.Name} has been slain! Under The Castle has been conquered! Victory! ###";
            }
        }

        if (stageToUnlock > 0)
        {
            await OpenGateAsync(stageToUnlock);
            await RewardParticipantsAsync(stageToUnlock, npc.X, npc.Z);
        }

        if (victory)
        {
            await RewardParticipantsAsync(4, npc.X, npc.Z);
            SpawnVictoryNpcs();
        }

        if (noticeMessage != null)
        {
            await BroadcastStageNoticeAsync(noticeMessage);
        }
    }

    public async Task TickAsync()
    {
        if (!IsActive)
            return;

        bool timeExpired = false;
        bool victoryElapsed = false;

        lock (_stateLock)
        {
            if (_remainingSeconds > 0)
            {
                _remainingSeconds--;
                if (_remainingSeconds == 0)
                    timeExpired = true;
            }

            if (_finalBossKilled && _finishedAtUtc.HasValue)
            {
                var elapsed = (DateTime.UtcNow - _finishedAtUtc.Value).TotalSeconds;
                if (!_returnNoticeSent && elapsed >= (VictoryDelaySeconds - 10))
                {
                    _returnNoticeSent = true;
                    _ = BroadcastStageNoticeAsync("### [Under The Castle] Returning to Moradon in 10 seconds... ###");
                }

                if (elapsed >= VictoryDelaySeconds)
                    victoryElapsed = true;
            }
        }

        if (timeExpired || victoryElapsed)
        {
            await CloseAsync();
        }
    }

    private void SpawnUtcMonsters()
    {
        DespawnUtcMonsters();

        var positions = gameDataService.NpcPositions
            .Where(p => p.ZoneId == UtcZoneId && p.Room == 86)
            .ToList();

        int spawned = 0;
        foreach (var pos in positions)
        {
            var result = npcSpawnRowService.Spawn(pos);
            foreach (var npc in result)
            {
                npc.RespawnType = NpcRespawnType.Never;
            }
            spawned += result.Count;
        }

        logger.LogInformation("Spawned {Count} event monsters & gates for Under the Castle", spawned);
    }

    private void DespawnUtcMonsters()
    {
        var monsters = sessionManager.Regions.GetAllNpcs()
            .Where(n => n.ZoneId == UtcZoneId && (n.IsMonster || n.NpcId == VictoryNpcId))
            .ToList();

        foreach (var monster in monsters)
        {
            sessionManager.Regions.RemoveNpc(monster);
        }

        logger.LogInformation("Removed {Count} monsters from Under the Castle zone", monsters.Count);
    }

    private async Task OpenGateAsync(int gateStage)
    {
        var doors = sessionManager.Regions.GetAllNpcs()
            .Where(n => n.ZoneId == UtcZoneId && (
                (gateStage == 1 && (n.TrapNumber == 1 || n.NpcId == Gate1DoorNpcId)) ||
                (gateStage == 2 && (n.TrapNumber == 2 || n.NpcId == Gate2DoorNpcId)) ||
                (gateStage == 3 && (n.TrapNumber is 3 or 4 || n.NpcId == Gate3DoorNpcId))
            ))
            .ToList();

        foreach (var door in doors)
        {
            await npcLifecycleService.DespawnAsync(door);
            logger.LogInformation("Opened UTC gate: {NpcId} (Trap {Trap})", door.NpcId, door.TrapNumber);
        }
    }

    private void SpawnVictoryNpcs()
    {
        var proto = gameDataService.GetNpc(VictoryNpcId);
        if (proto == null)
            return;

        (float X, float Z)[] spots = [(852f, 830f), (825f, 873f)];
        foreach (var spot in spots)
        {
            var pos = new NpcPosData
            {
                ZoneId = UtcZoneId,
                NpcId = VictoryNpcId,
                LeftX = (int)spot.X,
                TopZ = (int)spot.Z,
                ActType = NpcPosData.NpcSpawnActTypeBase,
                NumNPC = 1,
                Room = 0
            };
            var result = npcSpawnRowService.Spawn(pos);
            foreach (var npc in result)
            {
                npc.RespawnType = NpcRespawnType.Never;
            }
        }
    }

    private static bool IsInCircle(float px, float pz, float cx, float cz, float radius)
    {
        var dx = px - cx;
        var dz = pz - cz;
        return (dx * dx + dz * dz) <= (radius * radius);
    }

    private async Task RewardParticipantsAsync(int stage, float bossX, float bossZ)
    {
        if (stage < 1 || stage >= StageRewards.Length)
            return;

        var trophyItem = gameDataService.GetItem(TrophyOfFlameItemId);
        if (trophyItem == null)
            return;

        var stageReward = StageRewards[stage];
        var players = sessionManager.GetAll()
            .Where(s => s.ZoneId == UtcZoneId)
            .ToList();

        foreach (var player in players)
        {
            bool inArea = IsInCircle(player.X, player.Z, stageReward.AreaX, stageReward.AreaZ, stageReward.AreaRadius);
            bool nearBoss = IsInCircle(player.X, player.Z, bossX, bossZ, BossRewardRadius);

            if (!inArea && !nearBoss)
                continue;

            int trophyCount = (inArea && nearBoss) ? 2 : 1;

            var grantedTrophies = await itemGrantService.GrantAsync(player, trophyItem, trophyCount);
            if (grantedTrophies > 0)
            {
                await player.Client.SendPacket(ChatPacketWriter.SystemNotice(
                    (byte)player.Nation, $"[Under The Castle] You received {grantedTrophies}x {trophyItem.Name}!"));
            }

            foreach (var itemId in stageReward.Items)
            {
                var extraItem = gameDataService.GetItem(itemId);
                if (extraItem != null)
                {
                    var granted = await itemGrantService.GrantAsync(player, extraItem, 1);
                    if (granted > 0)
                    {
                        await player.Client.SendPacket(ChatPacketWriter.SystemNotice(
                            (byte)player.Nation, $"[Under The Castle] You received {granted}x {extraItem.Name}!"));
                    }
                }
            }
        }
    }

    private async Task KickOutZoneUsersAsync()
    {
        var players = sessionManager.GetAll()
            .Where(s => s.ZoneId == UtcZoneId)
            .ToList();

        foreach (var player in players)
        {
            try
            {
                await zoneTransitionService.ChangeZoneAsync(player, (byte)ZoneId.Moradon, 0f, 0f);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warp {Name} from UTC to Moradon", player.Name);
            }
        }
    }

    private async Task BroadcastStageNoticeAsync(string message)
    {
        var karusNotice = ChatPacketWriter.SystemNotice((byte)AccountNation.Karus, message);
        var elmoNotice = ChatPacketWriter.SystemNotice((byte)AccountNation.ElMorad, message);

        foreach (var session in sessionManager.GetAll().Where(s => s.ZoneId == UtcZoneId))
        {
            try
            {
                var pkt = session.Nation == AccountNation.Karus ? karusNotice : elmoNotice;
                await session.Client.SendPacket(pkt);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send stage notice to {Name}", session.Name);
            }
        }
    }
}
