using LibreKO.Common.Enums;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public interface IBowlEventService
{
    BowlEventSettings Settings { get; }
    bool IsActive { get; }
    TimeSpan Remaining { get; }
    int IntervalSeconds { get; set; }
    int PlayersInBowl { get; }

    bool IsInBowl(byte zoneId, float x, float z);
    Task<bool> StartAsync(int? durationMinutes = null);
    Task StopAsync();
    Task TickAsync();
}

public class BowlEventService : IBowlEventService
{
    public const int MinIntervalSeconds = 5;

    private const byte BowlZone = (byte)ZoneId.RonarkLand;
    private const float BowlCenterX = 1014f;
    private const float BowlCenterZ = 1007f;
    private const float BowlRadius = 131f;
    private const int SecondsPerMinute = 60;
    private const int StartMinuteOfHour = 0;
    private const long Inactive = 0;
    private const string Tag = "[Bowl Event]";
    private const string EndedMessage = Tag + " The Bowl event has ended. Thank you for taking part.";

    private static readonly int[] CountdownMinutes = [45, 30, 15, 10, 5, 1];

    private sealed class PlayerBowlState
    {
        public float LastX { get; set; }
        public float LastZ { get; set; }
        public DateTimeOffset LastReward { get; set; }
        public bool AfkWarningSent { get; set; }
    }

    private readonly SessionManager sessionManager;
    private readonly ILoyaltyService loyaltyService;
    private readonly IPlayerProgressionService playerProgressionService;
    private readonly IUserNotificationService userNotificationService;
    private readonly ILogger<BowlEventService> logger;
    private readonly EventSettings events;
    private readonly ScheduleClock scheduleClock;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<int, PlayerBowlState> bowlStates = [];
    private readonly HashSet<int> warnedRemainingMinutes = [];
    private long endsAtTicks = Inactive;
    private int intervalSeconds;
    private int playersInBowl;
    private DateTimeOffset lastAnnouncement;
    private DateTime lastScheduledStart = DateTime.MinValue;

    public BowlEventService(
        SessionManager sessionManager,
        IOptions<GameServerSettings> settings,
        ILoyaltyService loyaltyService,
        IPlayerProgressionService playerProgressionService,
        IUserNotificationService userNotificationService,
        ScheduleClock scheduleClock,
        ILogger<BowlEventService> logger,
        TimeProvider? clock = null)
    {
        this.sessionManager = sessionManager;
        this.loyaltyService = loyaltyService;
        this.playerProgressionService = playerProgressionService;
        this.userNotificationService = userNotificationService;
        this.scheduleClock = scheduleClock;
        this.logger = logger;
        this.clock = clock ?? TimeProvider.System;
        events = settings.Value.Events;
        Settings = settings.Value.BowlEvent;
        intervalSeconds = Math.Max(MinIntervalSeconds, Settings.IntervalSeconds);
    }

    public BowlEventSettings Settings { get; }

    public bool IsActive => Volatile.Read(ref endsAtTicks) != Inactive;

    public TimeSpan Remaining
    {
        get
        {
            var ends = Volatile.Read(ref endsAtTicks);
            if (ends == Inactive)
                return TimeSpan.Zero;

            var left = new DateTimeOffset(ends, TimeSpan.Zero) - clock.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public int IntervalSeconds
    {
        get => Volatile.Read(ref intervalSeconds);
        set => Volatile.Write(ref intervalSeconds, Math.Max(MinIntervalSeconds, value));
    }

    public int PlayersInBowl => Volatile.Read(ref playersInBowl);

    public bool IsInBowl(byte zoneId, float x, float z)
    {
        var dx = x - BowlCenterX;
        var dz = z - BowlCenterZ;
        return zoneId == BowlZone && dx * dx + dz * dz <= BowlRadius * BowlRadius;
    }

    public async Task<bool> StartAsync(int? durationMinutes = null)
    {
        if (!Settings.Enabled)
            return false;

        var minutes = durationMinutes is > 0 ? durationMinutes.Value : Settings.DefaultDurationMinutes;
        await gate.WaitAsync();
        try
        {
            var now = clock.GetUtcNow();
            Volatile.Write(ref endsAtTicks, now.AddMinutes(minutes).UtcTicks);
            lastAnnouncement = now;
            warnedRemainingMinutes.Clear();
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Bowl event started for {Minutes} minutes", minutes);
        await BroadcastAsync($"{Tag} The Bowl event has begun in Ronark Land and runs for {MinutesText(minutes)}.");
        return true;
    }

    public async Task StopAsync()
    {
        bool stopped;
        await gate.WaitAsync();
        try
        {
            stopped = StopCore();
        }
        finally
        {
            gate.Release();
        }

        if (stopped)
            await AnnounceEndAsync();
    }

    public async Task TickAsync()
    {
        await TickScheduleAsync(ScheduleTime.Now(events, scheduleClock.UtcNow));
        if (!IsActive)
            return;

        await gate.WaitAsync();
        try
        {
            await TickCoreAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task<bool> TickScheduleAsync(DateTime scheduleNow)
    {
        var minute = scheduleNow.Date.AddHours(scheduleNow.Hour).AddMinutes(scheduleNow.Minute);
        if (IsActive || scheduleNow.Minute != StartMinuteOfHour || minute == lastScheduledStart
            || !Settings.StartHours.Contains(scheduleNow.Hour))
            return false;

        lastScheduledStart = minute;
        return await StartAsync();
    }

    private async Task TickCoreAsync()
    {
        var ends = Volatile.Read(ref endsAtTicks);
        if (ends == Inactive)
            return;

        var now = clock.GetUtcNow();
        var endsAt = new DateTimeOffset(ends, TimeSpan.Zero);
        if (now >= endsAt)
        {
            StopCore();
            await AnnounceEndAsync();
            return;
        }

        await AnnounceAsync(now, (int)Math.Ceiling((endsAt - now).TotalMinutes));
        await TickPlayersAsync(now);
        Volatile.Write(ref playersInBowl, bowlStates.Count);
    }

    private bool StopCore()
    {
        if (Interlocked.Exchange(ref endsAtTicks, Inactive) == Inactive)
            return false;

        bowlStates.Clear();
        warnedRemainingMinutes.Clear();
        Volatile.Write(ref playersInBowl, 0);
        return true;
    }

    private async Task AnnounceEndAsync()
    {
        logger.LogInformation("Bowl event ended");
        await BroadcastAsync(EndedMessage);
    }

    private async Task AnnounceAsync(DateTimeOffset now, int remainingMinutes)
    {
        if (CountdownMinutes.Contains(remainingMinutes) && warnedRemainingMinutes.Add(remainingMinutes))
        {
            lastAnnouncement = now;
            await BroadcastAsync($"{Tag} {MinutesText(remainingMinutes)} remaining.");
            return;
        }

        if (Settings.AnnouncementMinutes <= 0
            || now - lastAnnouncement < TimeSpan.FromMinutes(Settings.AnnouncementMinutes))
            return;

        lastAnnouncement = now;
        await BroadcastAsync(
            $"{Tag} The Bowl event in Ronark Land runs for another {MinutesText(remainingMinutes)}. " +
            $"Stand in the Bowl at level {Settings.MinLevel} or above to earn rewards.");
    }

    private async Task TickPlayersAsync(DateTimeOffset now)
    {
        var interval = TimeSpan.FromSeconds(IntervalSeconds);
        var online = new HashSet<int>();
        foreach (var player in sessionManager.GetAll())
        {
            if (player.IsBot)
                continue;

            online.Add(player.CharacterId);
            if (player.Hp <= 0 || player.Level < Settings.MinLevel)
            {
                bowlStates.Remove(player.CharacterId);
                continue;
            }

            if (!IsInBowl(player.ZoneId, player.X, player.Z))
            {
                if (bowlStates.Remove(player.CharacterId))
                    await SendNoticeAsync(player, $"{Tag} You left the Bowl.");
                continue;
            }

            if (!bowlStates.TryGetValue(player.CharacterId, out var state))
            {
                bowlStates[player.CharacterId] = new PlayerBowlState { LastX = player.X, LastZ = player.Z, LastReward = now };
                await SendNoticeAsync(player,
                    $"{Tag} You entered the Bowl. Keep moving to earn a reward {EveryText(IntervalSeconds)}.", onScreen: true);
                continue;
            }

            if (now - state.LastReward < interval)
                continue;

            if (IsIdle(player, state))
            {
                if (!state.AfkWarningSent)
                {
                    state.AfkWarningSent = true;
                    await SendNoticeAsync(player,
                        $"{Tag} You have not moved. Move around inside the Bowl to receive rewards.", onScreen: true);
                }
                continue;
            }

            state.AfkWarningSent = false;
            state.LastX = player.X;
            state.LastZ = player.Z;
            state.LastReward = now;
            await RewardAsync(player);
        }

        foreach (var characterId in bowlStates.Keys.Where(id => !online.Contains(id)).ToList())
            bowlStates.Remove(characterId);
    }

    private bool IsIdle(UserSession player, PlayerBowlState state)
    {
        if (!Settings.AntiAfkEnabled)
            return false;

        var dx = player.X - state.LastX;
        var dz = player.Z - state.LastZ;
        var minMove = Settings.AntiAfkMoveDistance;
        return dx * dx + dz * dz < minMove * minMove;
    }

    private async Task RewardAsync(UserSession player)
    {
        var (cash, coins) = player.WithLock(s =>
        {
            var cashBefore = s.KnightCash;
            s.KnightCash = (int)Math.Clamp((long)s.KnightCash + Settings.KnightCash, 0L, int.MaxValue);
            var coinsAdded = (int)Math.Clamp((long)ExchangePacketConstants.CoinMax - s.Money, 0L, Settings.Gold);
            s.Money += coinsAdded;
            return (s.KnightCash - cashBefore, coinsAdded);
        });

        var gains = new List<string>();
        if (cash > 0)
        {
            await player.Client.SendPacket(ShoppingMallStoreService.BalancePacket(player.KnightCash));
            gains.Add($"+{cash:N0} KC");
        }

        if (Settings.Loyalty > 0)
        {
            await loyaltyService.ChangeAsync(player, Settings.Loyalty);
            gains.Add($"+{Settings.Loyalty:N0} NP");
        }

        if (coins > 0)
        {
            await userNotificationService.SendGoldGainAsync(player, coins);
            gains.Add($"+{coins:N0} Noah");
        }

        if (Settings.Exp > 0)
        {
            await playerProgressionService.AwardExperienceAsync(player, Settings.Exp);
            gains.Add($"+{Settings.Exp:N0} EXP");
        }

        if (gains.Count > 0)
            await SendNoticeAsync(player, $"{Tag} Reward: {string.Join(", ", gains)}.");
    }

    private async Task BroadcastAsync(string message)
    {
        await sessionManager.BroadcastToAll(NoticePacketWriter.Screen(message));
        await sessionManager.BroadcastToAll(ChatPacketWriter.SystemNotice((byte)AccountNation.None, message));
    }

    private static async Task SendNoticeAsync(UserSession player, string message, bool onScreen = false)
    {
        await player.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)player.Nation, message));
        if (onScreen)
            await player.Client.SendPacket(NoticePacketWriter.Screen(message));
    }

    private static string MinutesText(int minutes) => minutes == 1 ? "1 minute" : $"{minutes} minutes";

    private static string EveryText(int seconds) => seconds switch
    {
        SecondsPerMinute => "every minute",
        _ when seconds % SecondsPerMinute == 0 => $"every {seconds / SecondsPerMinute} minutes",
        _ => $"every {seconds} seconds",
    };
}
