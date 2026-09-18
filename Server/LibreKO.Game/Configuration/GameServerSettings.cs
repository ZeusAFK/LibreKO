using System.ComponentModel.DataAnnotations;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Configuration;

public class GameServerSettings
{
    public const string SectionName = "GameServer";

    public string BindHost { get; set; } = "*";

    [Range(1, 65535)]
    public int BindPort { get; set; } = default!;

    [Range(1, int.MaxValue)]
    public int ServerId { get; set; } = 1;

    public int Version { get; set; } = default!;
    public string MapDirectory { get; set; } = "Map";
    public string QuestsDirectory { get; set; } = "Quests";
    public string? QuestManifest { get; set; }
    public WelcomeSettings Welcome { get; set; } = new();
    public PlayerSettings Player { get; set; } = new();
    public MonsterSettings Monsters { get; set; } = new();
    public GlobalSettings Global { get; set; } = new();
    public EventSettings Events { get; set; } = new();

    public PublicDemoSettings PublicDemo { get; set; } = new();

    public SeedingSettings Seeding { get; set; } = new();

    public ConnectionLimitsSettings Connections { get; set; } = new();
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

    public int[] ChaosStartHours { get; set; } = [];

    public int[] BorderDefenseWarStartHours { get; set; } = [];

    public int[] JuraidMountainStartHours { get; set; } = [];
}
