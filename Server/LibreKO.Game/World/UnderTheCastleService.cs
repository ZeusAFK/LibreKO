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
    void Close();
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
    public const int DefaultDurationMinutes = 60;
    public const int VictoryDelaySeconds = 60;

    // Boss IDs from TRANCE reference / K_MONSTER
    public const int EmperorMammothNpcId = 9501;
    public const int CreshergimmicNpcId = 9507;
    public const int PuriousMiniNpcId = 9566;
    public const int PuriousInvisibleNpcId = 9512;
    public const int FluwitonFinalBossNpcId = 9515;

    // Gate / Door NPC IDs & Trap Numbers
    public const int Gate1DoorNpcId = 9550;
    public const int Gate2DoorNpcId = 9561;
    public const int Gate3DoorNpcId = 9562;
    public const int VictoryNpcId = 29197;

    private const float UtcCampX = 69f;
    private const float UtcCampZ = 64f;
    private const float MoradonTownX = 816f;
    private const float MoradonTownZ = 532f;

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

            var minutes = durationMinutes ?? DefaultDurationMinutes;
            _remainingSeconds = minutes * 60;
            _isActive = true;
            _currentStage = 1;
            _finalBossKilled = false;
            _finishedAtUtc = null;
            _returnNoticeSent = false;

            logger.LogInformation("Under the Castle event started for {Minutes} minutes", minutes);
        }

        SpawnUtcMonsters();
        _ = BroadcastNoticeAsync("### [Under The Castle] Under the Castle has opened! You may now enter Under The Castle. ###");
    }

    public void Close()
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
        _ = BroadcastNoticeAsync("### [Under The Castle] Under The Castle is now over. ###");

        // Kick all players in zone 86 back to Moradon
        _ = KickOutZoneUsersAsync();

        // Despawn event monsters & gates in zone 86
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
                noticeMessage = "### [Under The Castle] Emperor Mammoth defeated! Gate 1 is now OPEN! ###";
            }
            else if (npc.NpcId == CreshergimmicNpcId && _currentStage == 2)
            {
                _currentStage = 3;
                stageToUnlock = 2;
                noticeMessage = "### [Under The Castle] Creshergimmic defeated! Gate 2 is now OPEN! ###";
            }
            else if ((npc.NpcId == PuriousMiniNpcId || npc.NpcId == PuriousInvisibleNpcId) && _currentStage == 3)
            {
                _currentStage = 4;
                stageToUnlock = 3;
                noticeMessage = "### [Under The Castle] Purious defeated! Final Chamber Gate is now OPEN! ###";
            }
            else if (npc.NpcId == FluwitonFinalBossNpcId)
            {
                _finalBossKilled = true;
                _finishedAtUtc = DateTime.UtcNow;
                victory = true;
                noticeMessage = "### [Under The Castle] You have eliminated all the monsters in the temple! ###";
            }
        }

        if (stageToUnlock > 0)
        {
            await OpenGateAsync(stageToUnlock);
            await RewardParticipantsAsync(stageToUnlock, 1);
        }

        if (victory)
        {
            await RewardParticipantsAsync(4, 2);
            SpawnVictoryNpcs();
        }

        if (noticeMessage != null)
        {
            await BroadcastNoticeAsync(noticeMessage);
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
                    _ = BroadcastNoticeAsync("### [Under The Castle] Returning to Moradon in 10 seconds... ###");
                }

                if (elapsed >= VictoryDelaySeconds)
                    victoryElapsed = true;
            }
        }

        if (timeExpired || victoryElapsed)
        {
            Close();
        }
    }

    private void SpawnUtcMonsters()
    {
        // First clean up any existing monsters in Zone 86
        DespawnUtcMonsters();

        // Spawn all positions for Zone 86 where Room == 86
        var positions = gameDataService.NpcPositions
            .Where(p => p.ZoneId == UtcZoneId && p.Room == 86)
            .ToList();

        int spawned = 0;
        foreach (var pos in positions)
        {
            var result = npcSpawnRowService.Spawn(pos);
            spawned += result.Count;
        }

        logger.LogInformation("Spawned {Count} event monsters & gates for Under the Castle", spawned);
    }

    private void DespawnUtcMonsters()
    {
        var monsters = sessionManager.Regions.GetAllNpcs()
            .Where(n => n.ZoneId == UtcZoneId && n.IsMonster)
            .ToList();

        foreach (var monster in monsters)
        {
            sessionManager.Regions.RemoveNpc(monster);
        }

        logger.LogInformation("Removed {Count} monsters from Under the Castle zone", monsters.Count);
    }

    private async Task OpenGateAsync(int gateStage)
    {
        // Gate 1: TrapNumber 1 (Door 9550)
        // Gate 2: TrapNumber 2 (Door 9561)
        // Gate 3: TrapNumber 3 & 4 (Door 9562)
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

        // Spawn victory chest/guard NPCs at final room
        (float X, float Z)[] spots = [(852f, 830f), (825f, 873f)];
        foreach (var spot in spots)
        {
            var pos = new NpcPosData
            {
                ZoneId = UtcZoneId,
                NpcId = VictoryNpcId,
                LeftX = (int)spot.X,
                TopZ = (int)spot.Z,
                ActType = 1,
                NumNPC = 1,
                Room = 0
            };
            npcSpawnRowService.Spawn(pos);
        }
    }

    private async Task RewardParticipantsAsync(int stage, int itemCount)
    {
        var trophyItem = gameDataService.GetItem(TrophyOfFlameItemId);
        if (trophyItem == null)
            return;

        var players = sessionManager.GetAll()
            .Where(s => s.ZoneId == UtcZoneId)
            .ToList();

        foreach (var player in players)
        {
            try
            {
                var granted = await itemGrantService.GrantAsync(player, trophyItem, itemCount);
                if (granted > 0)
                {
                    await player.Client.SendPacket(ChatPacketWriter.SystemNotice(
                        (byte)player.Nation, $"[Under The Castle] You received {granted}x {trophyItem.Name} for clearing Stage {stage}!"));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to grant UTC reward to {Name}", player.Name);
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
                await zoneTransitionService.ChangeZoneAsync(player, (byte)ZoneId.Moradon, MoradonTownX, MoradonTownZ);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warp {Name} from UTC to Moradon", player.Name);
            }
        }
    }

    private Task BroadcastNoticeAsync(string message)
    {
        var karusNotice = ChatPacketWriter.SystemNotice((byte)AccountNation.Karus, message);
        var elmoNotice = ChatPacketWriter.SystemNotice((byte)AccountNation.ElMorad, message);

        foreach (var session in sessionManager.GetAll())
        {
            try
            {
                var pkt = session.Nation == AccountNation.Karus ? karusNotice : elmoNotice;
                _ = session.Client.SendPacket(pkt);
            }
            catch
            {
                // best effort
            }
        }

        return Task.CompletedTask;
    }
}
