using System.ComponentModel.DataAnnotations;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Configuration;

public class GameServerSettings
{
    public const string SectionName = "GameServer";
    public const int DefaultBotSimControlPort = 15990;

    public string BindHost { get; set; } = "*";

    public string MinimumClientVersion { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int BindPort { get; set; } = default!;

    [Range(1, int.MaxValue)]
    public int ServerId { get; set; } = 1;

    public int Version { get; set; } = default!;
    public string MapDirectory { get; set; } = "Map";
    public string QuestsDirectory { get; set; } = "Quests";
    public string? QuestManifest { get; set; }
    public int SpecialAuctionGroup { get; set; } = 1;
    public int BotSimControlPort { get; set; } = DefaultBotSimControlPort;
    public WelcomeSettings Welcome { get; set; } = new();
    public PlayerSettings Player { get; set; } = new();
    public MonsterSettings Monsters { get; set; } = new();
    public GlobalSettings Global { get; set; } = new();
    public EventSettings Events { get; set; } = new();
    public PartyVoteSettings PartyVote { get; set; } = new();
    public JackpotSettings Jackpot { get; set; } = new();
    public ChatRoomSettings ChatRooms { get; set; } = new();
    public BowlEventSettings BowlEvent { get; set; } = new();

    public PublicDemoSettings PublicDemo { get; set; } = new();

    public SeedingSettings Seeding { get; set; } = new();

    public ConnectionLimitsSettings Connections { get; set; } = new();

    public LoginThrottleSettings Logins { get; set; } = new();
}

public class SeedingSettings
{
    public bool Force { get; set; }
}

public class PublicDemoSettings
{
    public bool GrantGameMasterPanelToEveryone { get; set; }
    public bool GrantGameMasterSpeedToEveryone { get; set; }
    public bool GrantSetLevelToEveryone { get; set; }
}

public class MonsterSettings
{
    public float CloseAggroRange { get; set; }
    public bool AggressiveBosses { get; set; } = true;
    public int[] AggressiveZones { get; set; } = [];
    public int[] AggressiveNpcIds { get; set; } = [];
}

public class WelcomeSettings
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class PlayerSettings
{
    public string NamePattern { get; set; } = "[A-Za-z]*";

    [Range(1, 83)]
    public int MaxLevel { get; set; } = 83;

    [Range(0, int.MaxValue)]
    public int MaxOnline { get; set; }

    [Range(1, int.MaxValue)]
    public int AutoSaveDelaySeconds { get; set; } = 900;

    [Range(1, int.MaxValue)]
    public int HpGainDelaySeconds { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int MpGainDelaySeconds { get; set; } = 5;
}

public class GlobalSettings
{
    public string[] BannedWords { get; set; } = [];
    public bool UseDataCache { get; set; }
    public int ThreadPoolSize { get; set; }

    public int MinWorkerThreads { get; set; }

    public int MaxConcurrentScripts { get; set; }

    public bool HotReloadQuestScripts { get; set; } = true;

    public int MovementBroadcastHz { get; set; }
    public int ExpMultiplier { get; set; } = 1;
    public int KillRate { get; set; } = 1;
    public AnvilSettings Anvil { get; set; } = new();
}

public class AnvilSettings
{
    public bool Enabled { get; set; }
    public double MaxRateSwingPercent { get; set; } = 10;
    public double SuccessStepPercent { get; set; } = 0.1;
    public double FailureStepPercent { get; set; } = 0.1;
}

public class JackpotSettings
{
    public int[] ExpRates { get; set; } = [1100, 200, 50, 25, 10, 5];
    public int[] CoinRates { get; set; } = [1100, 200, 50, 25, 10, 5];
}

public class ChatRoomSettings
{
    [Range(1, int.MaxValue)]
    public int HistoryDays { get; set; } = 90;

    public string NotesPath { get; set; } = "voicenotes";

    [Range(1, int.MaxValue)]
    public int NoteMaxSeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int NoteMaxBytes { get; set; } = 320 * 1024;

    [Range(1, int.MaxValue)]
    public int NoteMinIntervalSeconds { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int NotesPerDay { get; set; } = 200;

    [Range(0, int.MaxValue)]
    public int KeptRoomsPerCharacter { get; set; } = 3;

    public VoiceSettings Voice { get; set; } = new();
}

public class VoiceSettings
{
    public bool Enabled { get; set; } = true;
    public string BindHost { get; set; } = "*";

    [Range(1, 65535)]
    public int UdpPort { get; set; } = 15002;

    [Range(0, int.MaxValue)]
    public int MaxVoicePerRoom { get; set; }
}

public class PartyVoteSettings
{
    [Range(1, short.MaxValue)]
    public int WarningLimit { get; set; } = 3;

    [Range(1, int.MaxValue)]
    public int WarningDays { get; set; } = 7;
}

public class EventSettings
{
    [Range(1, int.MaxValue)]
    public int BattleIntervalMinutes { get; set; } = 120;  // How often wars auto-open

    [Range(1, int.MaxValue)]
    public int BattleDurationMinutes { get; set; } = 20;   // How long a war lasts

    [Range(0, int.MaxValue)]
    public int MinPlayersForWar { get; set; } = 10;        // Min online players to auto-open

    [Range(0, int.MaxValue)]
    public int BattleWinLoyalty { get; set; } = 500;        // NP reward for winning side

    public string ScheduleTimeZone { get; set; } = "Europe/Berlin";

    public int[] KrowazStartHours { get; set; } = [9, 22];

    public int[] BifrostStartHours { get; set; } = [1, 5, 13, 17, 20];

    public bool ChaosNeedsMap { get; set; }
}

public class BowlEventSettings
{
    public bool Enabled { get; set; } = true;

    [Range(5, int.MaxValue)]
    public int IntervalSeconds { get; set; } = 60;

    [Range(1, int.MaxValue)]
    public int DefaultDurationMinutes { get; set; } = 60;

    public int[] StartHours { get; set; } = [14, 19];

    [Range(0, int.MaxValue)]
    public int AnnouncementMinutes { get; set; } = 5;

    [Range(1, 83)]
    public byte MinLevel { get; set; } = 60;

    public bool AntiAfkEnabled { get; set; } = true;

    [Range(0.0, double.MaxValue)]
    public float AntiAfkMoveDistance { get; set; } = 2f;

    [Range(0, int.MaxValue)]
    public int KnightCash { get; set; } = 5;

    [Range(0, int.MaxValue)]
    public int Loyalty { get; set; } = 50;

    [Range(0, int.MaxValue)]
    public int Gold { get; set; } = 50000;

    [Range(0, long.MaxValue)]
    public long Exp { get; set; } = 250000;
}
