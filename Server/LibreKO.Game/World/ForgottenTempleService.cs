using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;

namespace LibreKO.Game.World;

public enum ForgottenTempleTier : byte
{
    LowLevel = 1,
    HighLevel = 2
}

public interface IForgottenTempleService
{
    bool IsActive { get; }
    long RemainingSeconds { get; }
    int CurrentWave { get; }
    ForgottenTempleTier CurrentTier { get; }
    byte MinLevel { get; }
    byte MaxLevel { get; }

    void Start(int? durationMinutes = null, byte minLevel = TempleEventRules.ForgottenTempleHighMinLevel, byte maxLevel = TempleEventRules.ForgottenTempleHighMaxLevel);
    Task CloseAsync();
    Task EnterAsync(UserSession session);
    Task OnNpcKilledAsync(NpcInstance npc, UserSession killer);
    Task TickAsync();
    Task<bool> ForceSpawnWaveAsync(int waveNumber);
}

public sealed class ForgottenTempleService(
    SessionManager sessionManager,
    IGameDataService gameDataService,
    IZoneTransitionService zoneTransitionService,
    INpcSummonService npcSummonService,
    IPlayerProgressionService playerProgressionService,
    IUserNotificationService userNotificationService,
    ILoyaltyService loyaltyService,
    ILogger<ForgottenTempleService> logger) : IForgottenTempleService
{
    public const byte FtZoneId = (byte)ZoneId.ForgottenTemple;
    public const int DefaultDurationMinutes = TempleEventRules.ForgottenTempleDurationSeconds / 60;
    public const int VictoryDelaySeconds = 60;
    public const int PreWaveNoticeSeconds = 10;

    private const float CenterX = 150f;
    private const float CenterZ = 150f;

    private readonly object _stateLock = new();
    private bool _isActive;
    private long _remainingSeconds;
    private long _totalDurationSeconds;
    private int _currentWave;
    private bool _finalBossKilled;
    private DateTime? _finishedAtUtc;
    private bool _returnNoticeSent;
    private bool _preWaveNoticeSent;
    private byte _minLevel = TempleEventRules.ForgottenTempleHighMinLevel;
    private byte _maxLevel = TempleEventRules.ForgottenTempleHighMaxLevel;
    private ForgottenTempleTier _currentTier = ForgottenTempleTier.HighLevel;
    private readonly HashSet<int> _lastWaveMonsterIds = [];
    private bool _lastWaveSpawned;

    public bool IsActive
    {
        get { lock (_stateLock) return _isActive; }
    }

    public long RemainingSeconds
    {
        get { lock (_stateLock) return _remainingSeconds; }
    }

    public int CurrentWave
    {
        get { lock (_stateLock) return _currentWave; }
    }

    public ForgottenTempleTier CurrentTier
    {
        get { lock (_stateLock) return _currentTier; }
    }

    public byte MinLevel
    {
        get { lock (_stateLock) return _minLevel; }
    }

    public byte MaxLevel
    {
        get { lock (_stateLock) return _maxLevel; }
    }

    public void Start(int? durationMinutes = null, byte minLevel = TempleEventRules.ForgottenTempleHighMinLevel, byte maxLevel = TempleEventRules.ForgottenTempleHighMaxLevel)
    {
        lock (_stateLock)
        {
            if (_isActive)
            {
                logger.LogWarning("Forgotten Temple start requested but event is already active");
                return;
            }

            var minutes = durationMinutes ?? DefaultDurationMinutes;
            _totalDurationSeconds = minutes * 60;
            _remainingSeconds = _totalDurationSeconds;
            _isActive = true;
            _currentWave = 0;
            _finalBossKilled = false;
            _finishedAtUtc = null;
            _returnNoticeSent = false;
            _preWaveNoticeSent = false;
            _lastWaveSpawned = false;
            _lastWaveMonsterIds.Clear();
            _minLevel = minLevel;
            _maxLevel = maxLevel;
            _currentTier = maxLevel <= TempleEventRules.ForgottenTempleLowMaxLevel ? ForgottenTempleTier.LowLevel : ForgottenTempleTier.HighLevel;

            logger.LogInformation("Forgotten Temple ({Tier}) event started for levels {Min}-{Max} ({Minutes} mins)",
                _currentTier, _minLevel, _maxLevel, minutes);
        }

        DespawnFtMonsters();
        string tierName = $"{_minLevel}-{_maxLevel}";
        _ = sessionManager.BroadcastToAll(NoticePacketWriter.Broadcast($"### [Forgotten Temple] Forgotten Temple ({tierName}) has opened! You may now enter the temple. ###"));
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
            _lastWaveMonsterIds.Clear();
            _lastWaveSpawned = false;
        }

        if (!wasActive)
            return;

        logger.LogInformation("Forgotten Temple event closed");

        await KickOutZoneUsersAsync();
        DespawnFtMonsters();
    }

    public async Task EnterAsync(UserSession session)
    {
        if (!IsActive && !session.IsGM)
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, "Forgotten Temple is currently closed."));
            return;
        }

        if (!session.IsGM && (session.Level < _minLevel || session.Level > _maxLevel))
        {
            await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
                (byte)session.Nation, $"Your level ({session.Level}) does not meet the requirements for this Forgotten Temple ({_minLevel}-{_maxLevel})."));
            return;
        }

        await zoneTransitionService.ChangeZoneAsync(session, FtZoneId, CenterX, CenterZ);
        await session.Client.SendPacket(ChatPacketWriter.SystemNotice(
            (byte)session.Nation, "### Welcome to Forgotten Temple! Defend the temple against waves of dark monsters! ###"));
    }

    public async Task OnNpcKilledAsync(NpcInstance npc, UserSession killer)
    {
        if (npc.ZoneId != FtZoneId || !IsActive)
            return;

        bool victory = false;
        lock (_stateLock)
        {
            if (_lastWaveSpawned && !_finalBossKilled)
            {
                _lastWaveMonsterIds.Remove(npc.UniqueId);
                if (_lastWaveMonsterIds.Count == 0)
                {
                    _finalBossKilled = true;
                    _finishedAtUtc = DateTime.UtcNow;
                    victory = true;
                }
            }
        }

        if (victory)
        {
            await BroadcastZoneNoticeAsync("### [Forgotten Temple] Victory! You have vanquished all monsters and protected the temple! ###");
            await RewardParticipantsAsync();
        }
    }

    public async Task TickAsync()
    {
        if (!IsActive)
            return;

        bool timeExpired = false;
        bool victoryElapsed = false;
        int nextWaveToSpawn = 0;

        lock (_stateLock)
        {
            if (_remainingSeconds > 0)
            {
                _remainingSeconds--;
                if (_remainingSeconds == 0)
                    timeExpired = true;
            }

            var elapsed = _totalDurationSeconds - _remainingSeconds;

            var waves = gameDataService.ForgottenTempleWaves

                .Where(w => w.Tier == (byte)_currentTier)
                .OrderBy(w => w.Wave)
                .ToList();

            var firstWave = waves.FirstOrDefault();
            int preWaveNoticeAt = firstWave != null ? Math.Max(0, firstWave.StartSecond - PreWaveNoticeSeconds) : 0;

            if (elapsed >= preWaveNoticeAt && !_preWaveNoticeSent)
            {
                _preWaveNoticeSent = true;
                _ = BroadcastZoneNoticeAsync($"### [Forgotten Temple] Warning: The dark seal is shattering! Monsters will emerge in {PreWaveNoticeSeconds} seconds! ###");
            }

            var nextWave = waves.FirstOrDefault(w => w.Wave > _currentWave && elapsed >= w.StartSecond);
            if (nextWave != null)
            {
                _currentWave = nextWave.Wave;
                nextWaveToSpawn = nextWave.Wave;
            }

            if (_finalBossKilled && _finishedAtUtc.HasValue)
            {
                var victoryTime = (DateTime.UtcNow - _finishedAtUtc.Value).TotalSeconds;
                if (!_returnNoticeSent && victoryTime >= (VictoryDelaySeconds - 10))
                {
                    _returnNoticeSent = true;
                    _ = BroadcastZoneNoticeAsync("### [Forgotten Temple] Returning to Moradon in 10 seconds... ###");
                }

                if (victoryTime >= VictoryDelaySeconds)
                    victoryElapsed = true;
            }
        }

        if (nextWaveToSpawn > 0)
        {
            await SpawnWaveAsync(nextWaveToSpawn);
        }

        if (timeExpired)
        {
            await BroadcastZoneNoticeAsync("### [Forgotten Temple] Time has expired! The seals of the temple have fallen. ###");
            await CloseAsync();
        }
        else if (victoryElapsed)
        {
            await CloseAsync();
        }
    }

    private async Task SpawnWaveAsync(int waveNumber)
    {
        var waves = gameDataService.ForgottenTempleWaves
            .Where(w => w.Tier == (byte)_currentTier && w.Wave == waveNumber)
            .ToList();

        if (waves.Count == 0)
            return;

        var allTierWaves = gameDataService.ForgottenTempleWaves
            .Where(w => w.Tier == (byte)_currentTier)
            .ToList();
        int maxWave = allTierWaves.Count > 0 ? allTierWaves.Max(w => w.Wave) : 0;
        bool isFinalWave = waveNumber == maxWave;

        int totalCount = waves.Sum(w => (int)w.Count);
        float angleStep = 360f / Math.Max(1, totalCount);
        float currentAngle = 0f;

        List<int> spawnedIds = [];

        foreach (var spawnDef in waves)
        {
            for (int i = 0; i < spawnDef.Count; i++)
            {
                float radius = Random.Shared.Next(10, 26);
                float rad = currentAngle * (MathF.PI / 180f);
                float x = CenterX + radius * MathF.Cos(rad);
                float z = CenterZ + radius * MathF.Sin(rad);
                currentAngle += angleStep;

                var summoned = await npcSummonService.SummonAsync(
                    spawnDef.NpcId, FtZoneId, 0, (int)x, (int)z, 1, 0f);

                if (isFinalWave)
                {
                    foreach (var npc in summoned)
                    {
                        spawnedIds.Add(npc.UniqueId);
                    }
                }
            }
        }

        if (isFinalWave)
        {
            lock (_stateLock)
            {
                _lastWaveSpawned = true;
                foreach (var id in spawnedIds)
                {
                    _lastWaveMonsterIds.Add(id);
                }
            }
        }

        await BroadcastZoneNoticeAsync($"### [Forgotten Temple] Wave {waveNumber}: Monsters emerge from the darkness! ###");
        logger.LogInformation("Forgotten Temple wave {Wave} spawned for Tier {Tier}", waveNumber, _currentTier);
    }

    public async Task<bool> ForceSpawnWaveAsync(int waveNumber)
    {
        if (!IsActive) return false;
        var waveDef = gameDataService.ForgottenTempleWaves
            .FirstOrDefault(w => w.Tier == (byte)_currentTier && w.Wave == waveNumber);
        if (waveDef == null) return false;

        lock (_stateLock)
        {
            _currentWave = waveNumber;
        }

        await SpawnWaveAsync(waveNumber);
        return true;
    }

    private void DespawnFtMonsters()
    {
        var monsters = sessionManager.Regions.GetAllNpcs()
            .Where(n => n.ZoneId == FtZoneId && n.IsMonster)
            .ToList();

        foreach (var monster in monsters)
        {
            sessionManager.Regions.RemoveNpc(monster);
        }

        logger.LogInformation("Removed {Count} monsters from Forgotten Temple zone", monsters.Count);
    }

    private async Task RewardParticipantsAsync()
    {
        var players = sessionManager.GetAll()
            .Where(s => s.ZoneId == FtZoneId)
            .ToList();

        foreach (var player in players)
        {
            try
            {
                var rewardRows = gameDataService.TempleEventRewards
                    .Where(r => r.Event == TempleEvent.ForgottenTemple
                        && r.Outcome == TempleEventRewardOutcome.Win
                        && ((r.MinLevel == 0 && r.MaxLevel == 0) || (player.Level >= r.MinLevel && (r.MaxLevel == 0 || player.Level <= r.MaxLevel))))
                    .ToList();

                foreach (var r in rewardRows)
                {
                    if (r.ItemId > 0 && r.ItemCount > 0)
                    {
                        await TempleEventHelpers.TryGiveItemAsync(
                            gameDataService, userNotificationService, player, r.ItemId, (ushort)r.ItemCount,
                            "Inventory is full! Could not receive Forgotten Temple reward item.");
                    }

                    if (r.LoyaltyPoints > 0)
                    {
                        await loyaltyService.ChangeAsync(player, r.LoyaltyPoints);
                    }

                    if (r.ExpPercent > 0)
                    {
                        long baseExp = TempleEventHelpers.CalculateBaseExp(player.Level);
                        long expAward = baseExp * r.ExpPercent / 100;
                        if (expAward > 0)
                            await playerProgressionService.AwardExperienceAsync(player, expAward);
                    }
                }

                await player.Client.SendPacket(ChatPacketWriter.SystemNotice(
                    (byte)player.Nation, "[Forgotten Temple] Congratulations! You have received your victory rewards."));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to grant Forgotten Temple reward to {Name}", player.Name);
            }
        }
    }

    private async Task KickOutZoneUsersAsync()
    {
        var players = sessionManager.GetAll()
            .Where(s => s.ZoneId == FtZoneId)
            .ToList();

        foreach (var player in players)
        {
            try
            {
                await zoneTransitionService.ChangeZoneAsync(player, (byte)ZoneId.Moradon, 0f, 0f);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to warp {Name} from FT to Moradon", player.Name);
            }
        }
    }

    private async Task BroadcastZoneNoticeAsync(string message)
    {
        var karusNotice = ChatPacketWriter.SystemNotice((byte)AccountNation.Karus, message);
        var elmoNotice = ChatPacketWriter.SystemNotice((byte)AccountNation.ElMorad, message);
        var screenNotice = NoticePacketWriter.Broadcast(message);

        foreach (var session in sessionManager.GetAll().Where(s => s.ZoneId == FtZoneId))
        {
            try
            {
                var pkt = session.Nation == AccountNation.Karus ? karusNotice : elmoNotice;
                await session.Client.SendPacket(pkt);
                await session.Client.SendPacket(screenNotice);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send zone notice to {Name}", session.Name);
            }
        }
    }
}
