using System.Collections.Concurrent;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LibreKO.Game.World;

public interface IBowlEventService
{
    bool IsActive { get; set; }
    int IntervalSeconds { get; set; }
    int DefaultDurationMinutes { get; set; }
    DateTime? EndTimeServer { get; set; }
    int RemainingSeconds { get; }
    byte ZoneId { get; set; }
    byte MinLevel { get; set; }
    float CenterX { get; set; }
    float CenterZ { get; set; }
    float Radius { get; set; }
    bool AntiAfkEnabled { get; set; }
    int KnightCash { get; set; }
    int Loyalty { get; set; }
    int Gold { get; set; }
    long Exp { get; set; }
    ushort VisualEffectId { get; set; }
    int PeriodicAnnouncementMinutes { get; set; }

    int ActivePlayersInBowl { get; }

    bool IsInBowl(byte zoneId, float x, float z);
    Task StartEventAsync(int? durationMinutes = null, bool announce = true);
    Task StopEventAsync(bool announce = true);
    Task SetActiveAsync(bool active, int? durationMinutes = null, bool announce = true);
    Task BroadcastScreenNoticeAsync(string message);
    Task TickAsync();
}

public class BowlEventService : IBowlEventService
{
    private sealed class PlayerBowlState
    {
        public float LastX { get; set; }
        public float LastZ { get; set; }
        public DateTime LastRewardServer { get; set; }
        public DateTime EnteredServer { get; set; }
        public bool NotifiedEntered { get; set; }
        public bool AfkWarningSent { get; set; }
    }

    private readonly SessionManager sessionManager;
    private readonly ILoyaltyService loyaltyService;
    private readonly IPlayerProgressionService playerProgressionService;
    private readonly IUserNotificationService userNotificationService;
    private readonly ILogger<BowlEventService> logger;

    private readonly ConcurrentDictionary<int, PlayerBowlState> bowlStates = new();
    private readonly HashSet<int> warnedRemainingMinutes = [];
    private DateTime lastPeriodicAnnouncementServer = DateTime.MinValue;

    public bool IsActive { get; set; }
    public int IntervalSeconds { get; set; }
    public int DefaultDurationMinutes { get; set; }
    public DateTime? EndTimeServer { get; set; }
    public byte ZoneId { get; set; }
    public byte MinLevel { get; set; }
    public float CenterX { get; set; }
    public float CenterZ { get; set; }
    public float Radius { get; set; }
    public bool AntiAfkEnabled { get; set; }
    public int KnightCash { get; set; }
    public int Loyalty { get; set; }
    public int Gold { get; set; }
    public long Exp { get; set; }
    public ushort VisualEffectId { get; set; }
    public int PeriodicAnnouncementMinutes { get; set; } = 5;

    // Remaining seconds based strictly on real Server Time (DateTime.Now)
    public int RemainingSeconds => EndTimeServer.HasValue
        ? Math.Max(0, (int)(EndTimeServer.Value - DateTime.Now).TotalSeconds)
        : 0;

    public int ActivePlayersInBowl => bowlStates.Count;

    public BowlEventService(
        SessionManager sessionManager,
        IOptions<GameServerSettings> settings,
        ILoyaltyService loyaltyService,
        IPlayerProgressionService playerProgressionService,
        IUserNotificationService userNotificationService,
        ILogger<BowlEventService> logger)
    {
        this.sessionManager = sessionManager;
        this.loyaltyService = loyaltyService;
        this.playerProgressionService = playerProgressionService;
        this.userNotificationService = userNotificationService;
        this.logger = logger;

        var config = settings.Value.BowlEvent;
        IsActive = config.Enabled;
        IntervalSeconds = config.IntervalSeconds > 0 ? config.IntervalSeconds : 60;
        DefaultDurationMinutes = config.DefaultDurationMinutes > 0 ? config.DefaultDurationMinutes : 60;
        ZoneId = config.ZoneId;
        MinLevel = config.MinLevel > 0 ? config.MinLevel : (byte)60;
        CenterX = config.CenterX;
        CenterZ = config.CenterZ;
        Radius = config.Radius > 0 ? config.Radius : 131.0f;
        AntiAfkEnabled = config.AntiAfkEnabled;
        KnightCash = config.KnightCash;
        Loyalty = config.Loyalty;
        Gold = config.Gold;
        Exp = config.Exp;
        VisualEffectId = config.VisualEffectId > 0 ? config.VisualEffectId : (ushort)1;

        if (IsActive)
        {
            EndTimeServer = DateTime.Now.AddMinutes(DefaultDurationMinutes);
        }
    }

    public async Task StartEventAsync(int? durationMinutes = null, bool announce = true)
    {
        var duration = durationMinutes is > 0 ? durationMinutes.Value : DefaultDurationMinutes;
        IsActive = true;
        var now = DateTime.Now;
        EndTimeServer = now.AddMinutes(duration);
        warnedRemainingMinutes.Clear();
        lastPeriodicAnnouncementServer = now;

        if (announce)
        {
            await BroadcastScreenNoticeAsync(
                $"[Bowl Event] BEGUN in Ronark Land! Duration: {duration} mins. Ends at {EndTimeServer:HH:mm} (Server Time).");
        }
    }

    public async Task StopEventAsync(bool announce = true)
    {
        if (!IsActive)
            return;

        IsActive = false;
        EndTimeServer = null;
        bowlStates.Clear();
        warnedRemainingMinutes.Clear();

        if (announce)
        {
            await BroadcastScreenNoticeAsync(
                $"[Bowl Event] ENDED at {DateTime.Now:HH:mm} (Server Time)! Thank you for participating.");
        }
    }

    public Task SetActiveAsync(bool active, int? durationMinutes = null, bool announce = true)
    {
        return active
            ? StartEventAsync(durationMinutes, announce)
            : StopEventAsync(announce);
    }

    public async Task BroadcastScreenNoticeAsync(string message)
    {
        var screenPacket = NoticePacketWriter.Screen(message);
        var chatPacket = ChatPacketWriter.SystemNotice(0, message);

        foreach (var player in sessionManager.GetAll())
        {
            await player.Client.SendPacket(screenPacket);
            await player.Client.SendPacket(chatPacket);
        }
    }

    public bool IsInBowl(byte zoneId, float x, float z)
    {
        if (zoneId != ZoneId)
            return false;

        // Radial circular zone check (default CenterX=1014, CenterZ=1007, Radius=131)
        var dx = x - CenterX;
        var dz = z - CenterZ;
        if ((dx * dx) + (dz * dz) <= (Radius * Radius))
            return true;

        // Bounding box coverage in Ronark Land bowl
        return x is >= 892.0f and <= 1166.0f && z is >= 969.0f and <= 1050.0f;
    }

    public async Task TickAsync()
    {
        if (!IsActive)
        {
            if (!bowlStates.IsEmpty)
                bowlStates.Clear();
            return;
        }

        var now = DateTime.Now;

        // Auto-expiration when duration based on Server Time ends
        if (EndTimeServer.HasValue && now >= EndTimeServer.Value)
        {
            await StopEventAsync(announce: true);
            return;
        }

        // Remaining time screen countdown alerts (measured in real server minutes)
        if (EndTimeServer.HasValue)
        {
            var remainingMinutes = (int)Math.Ceiling((EndTimeServer.Value - now).TotalMinutes);
            int[] countdownPoints = [45, 30, 15, 10, 5, 1];
            if (countdownPoints.Contains(remainingMinutes) && warnedRemainingMinutes.Add(remainingMinutes))
            {
                var unit = remainingMinutes == 1 ? "minute" : "minutes";
                await BroadcastScreenNoticeAsync(
                    $"[Bowl Event] {remainingMinutes} {unit} remaining! (Ends at {EndTimeServer:HH:mm} Server Time)");
            }
        }

        // Periodic server-wide screen announcement to all players
        if (PeriodicAnnouncementMinutes > 0 &&
            (lastPeriodicAnnouncementServer == DateTime.MinValue ||
             (now - lastPeriodicAnnouncementServer).TotalMinutes >= PeriodicAnnouncementMinutes))
        {
            lastPeriodicAnnouncementServer = now;
            var remainingText = EndTimeServer.HasValue
                ? $" (Ends at {EndTimeServer:HH:mm} Server Time, Sisa: {Math.Max(1, (int)(EndTimeServer.Value - now).TotalMinutes)}m)"
                : "";
            var periodicMsg = $"[Bowl Event] ACTIVE in Ronark Land{remainingText}! Stand in The Bowl (Lv {MinLevel}+) for KC, NP & Noah!";
            await BroadcastScreenNoticeAsync(periodicMsg);
        }

        var players = sessionManager.GetAll();

        foreach (var player in players)
        {
            if (player.IsBot || player.ZoneId != ZoneId || player.Hp <= 0 || player.Level < MinLevel)
            {
                bowlStates.TryRemove(player.CharacterId, out _);
                continue;
            }

            if (IsInBowl(player.ZoneId, player.X, player.Z))
            {
                if (!bowlStates.TryGetValue(player.CharacterId, out var state))
                {
                    state = new PlayerBowlState
                    {
                        LastX = player.X,
                        LastZ = player.Z,
                        LastRewardServer = now,
                        EnteredServer = now,
                        NotifiedEntered = true,
                        AfkWarningSent = false
                    };
                    bowlStates[player.CharacterId] = state;
                    await SendNoticeAsync(player, "[Bowl Event] You entered The Bowl! Stand your ground and stay active to earn rewards every minute.", showOnScreen: true);
                }
                else
                {
                    if ((now - state.LastRewardServer).TotalSeconds >= IntervalSeconds)
                    {
                        var dx = player.X - state.LastX;
                        var dz = player.Z - state.LastZ;
                        var distSq = (dx * dx) + (dz * dz);

                        // Anti-AFK check: player must have moved at least 2 units (~1.41m) during interval
                        if (AntiAfkEnabled && distSq < 4.0f)
                        {
                            if (!state.AfkWarningSent)
                            {
                                await SendNoticeAsync(player, "[Bowl Event] AFK detected! You must move and stay active inside The Bowl to receive rewards.", showOnScreen: true);
                                state.AfkWarningSent = true;
                            }
                        }
                        else
                        {
                            state.AfkWarningSent = false;
                            state.LastX = player.X;
                            state.LastZ = player.Z;
                            state.LastRewardServer = now;

                            await DistributeRewardAsync(player);
                        }
                    }
                }
            }
            else
            {
                if (bowlStates.TryRemove(player.CharacterId, out _))
                {
                    await SendNoticeAsync(player, "[Bowl Event] You left The Bowl.", showOnScreen: true);
                }
            }
        }
    }

    private async Task DistributeRewardAsync(UserSession player)
    {
        if (KnightCash > 0)
        {
            player.KnightCash += KnightCash;
            await player.Client.SendPacket(ShoppingMallStoreService.BalancePacket(player.KnightCash));
        }

        if (Loyalty > 0)
        {
            await loyaltyService.ChangeAsync(player, Loyalty);
        }

        if (Gold > 0)
        {
            player.Money = Math.Min(int.MaxValue, player.Money + Gold);
            await userNotificationService.SendGoldGainAsync(player, Gold);
        }

        if (Exp > 0)
        {
            await playerProgressionService.AwardExperienceAsync(player, Exp);
        }

        // Play character visual sparkle effect to all players in the region
        var effectPacket = MiningPacketWriter.AttemptSucceeded(2, 1, player.CharacterId, VisualEffectId);
        await sessionManager.Regions.SendToRegion(player, effectPacket, excludeSender: false);

        var rewardDetails = $"+{KnightCash} KC, +{Loyalty} NP, +{Gold:N0} Noah, +{Exp:N0} EXP";
        await SendNoticeAsync(player, $"[Bowl Event] Reward received! ({rewardDetails})", showOnScreen: true);
        logger.LogDebug("[Bowl Event] Granted reward to {PlayerName} ({Details})", player.Name, rewardDetails);
    }

    private static async Task SendNoticeAsync(UserSession session, string message, bool showOnScreen = false)
    {
        await session.Client.SendPacket(ChatPacketWriter.SystemNotice((byte)session.Nation, message));
        if (showOnScreen)
        {
            await session.Client.SendPacket(NoticePacketWriter.Screen(message));
        }
    }
}
