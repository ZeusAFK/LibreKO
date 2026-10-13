using System.IO;
using System.Globalization;
using Godot;
using LibreKO.Domain;

namespace LibreKO;

public static class Config
{
    private static bool _loaded;

    public static string ServerHost { get; private set; } = "127.0.0.1";
    public static string PatchUrl { get; private set; } = "";
    public static int ServerPort { get; private set; } = 15100;

    public static int GamePort { get; private set; } = 15001;

    public static int ServerVersion { get; private set; } = 0;

    public static bool PingEnabled { get; private set; } = true;

    public static bool Development { get; private set; }
    public static bool LegacyQuestFallback { get; private set; } = true;

    public static string PluginDirs { get; private set; } = "";
    private static readonly Dictionary<string, bool> _pluginEnabled = new(System.StringComparer.OrdinalIgnoreCase);

    public static bool PluginEnabled(string id) => _pluginEnabled.TryGetValue(id, out bool on) && on;

    public static void SetPluginEnabled(string id, bool enabled)
    {
        _pluginEnabled[id] = enabled;
        Save();
    }

    public static string GameHostFor(string advertisedIp) => Development ? "127.0.0.1" : advertisedIp;

    public enum VideoMode { Windowed, BorderlessFullscreen, Fullscreen }

    public static LibreKO.Network.GameLanguage Language { get; private set; } = LibreKO.Network.GameLanguage.English;
    public static VideoMode WindowMode { get; private set; } = VideoMode.Windowed;
    public static bool VSync { get; private set; } = true;
    public static int WinWidth { get; private set; } = 1280;
    public static int WinHeight { get; private set; } = 720;

    public const float UiScaleMin = 0.75f;
    public const float UiScaleMax = 3.0f;
    public const float UiScaleStep = 0.05f;
    public const float UiScaleMobileDefault = 1.6f;

    public static float UiScaleTouchDefault =>
        Mathf.Clamp(UiScaleMobileDefault * Platform.PhoneScale, UiScaleMin, UiScaleMax);

    public static float UiScale { get; private set; } = 1f;

    public const float NamePlateScaleMin = 0.75f;
    public const float NamePlateScaleMax = 2.0f;
    public const float NamePlateScaleStep = 0.05f;

    public static float NamePlateScale { get; private set; } = 1.25f;



    public const float StickSensitivityMin = 0.5f;
    public const float StickSensitivityMax = 2.0f;
    public const float StickSensitivityStep = 0.05f;

    public static float MoveStickSensitivity { get; private set; } = 1f;
    public static float LookStickSensitivity { get; private set; } = 1f;

    public enum AaMode { Off, Fxaa, Msaa2X, Msaa4X }
    public enum UpscaleMode { Off, Fsr1, Fsr2 }
    public enum UpscaleLevel { UltraQuality, Quality, Balanced, Performance, UltraPerformance }
    public enum FpsCap { Unlimited, Fps30, Fps60, Fps120, Fps144, Fps240 }
    public enum RenderDriver { Direct3D12, Vulkan }
    public const int CurrentScreen = -1;
    public static QualityLevel ShadowQuality { get; private set; } = QualityLevel.Medium;
    public static QualityLevel SsaoQuality { get; private set; } = QualityLevel.Medium;
    public static bool Shadows => ShadowQuality != QualityLevel.Off;
    public static bool Ssao => SsaoQuality != QualityLevel.Off;
    public static RenderDriver Renderer { get; private set; } = RenderDriver.Direct3D12;
    public static int Screen { get; private set; } = CurrentScreen;
    public static bool ShowTitles { get; private set; } = true;
    public static bool QuestNotices { get; private set; } = true;
    public static CospreKinds HiddenCospre { get; private set; } = CospreKinds.None;
    public static event System.Action? PlatesChanged;
    public static int UiAlpha { get; private set; } = LibreKO.Domain.UiOpacityRules.Opaque;
    public static event System.Action? UiAlphaChanged;
    public static event System.Action? CospreChanged;
    public static bool VolumetricFog { get; private set; } = false;
    public static bool Bloom { get; private set; } = true;
    public static bool FxLayer { get; private set; } = true;
    public static LibreKO.Domain.CrowdMode Crowd { get; private set; } = LibreKO.Domain.CrowdMode.Balanced;
    public static LibreKO.Domain.ShineStyle Shine { get; private set; } = LibreKO.Domain.ShineStyle.Glow;
    public static bool Clouds { get; private set; } = true;
    public static bool Capes { get; private set; } = true;

    public const float ViewDistanceMin = 0.35f;
    public const float ViewDistanceMax = 1f;
    public const float ViewDistanceMobileDefault = 0.4f;
    public const FpsCap FpsMobileDefault = FpsCap.Fps30;

    public static float ViewDistance { get; private set; } = 1f;
    public static int Brightness { get; private set; } = LightRange.Neutral;
    public static int Contrast { get; private set; } = LightRange.Neutral;
    public static int NightBrightness { get; private set; } = LightRange.Neutral;
    public static int Fog { get; private set; } = LightRange.Neutral;

    public const float FsrSharpness = 0.2f;

    public static Platform.UiMode TouchUi { get; private set; } = Platform.UiMode.Auto;
    public static AaMode AntiAlias { get; private set; } = AaMode.Msaa2X;
    public static UpscaleMode Upscale { get; private set; } = UpscaleMode.Off;
    public static UpscaleLevel UpscaleQuality { get; private set; } = UpscaleLevel.Quality;
    public static FpsCap FpsLimit { get; private set; } = FpsCap.Fps120;

    public static float RenderScale => Upscale == UpscaleMode.Off ? 1f : ScaleFor(UpscaleQuality);

    public static float ScaleFor(UpscaleLevel level) => level switch
    {
        UpscaleLevel.UltraQuality => 1f / 1.3f,
        UpscaleLevel.Balanced => 1f / 1.7f,
        UpscaleLevel.Performance => 0.5f,
        UpscaleLevel.UltraPerformance => 1f / 3f,
        _ => 1f / 1.5f,
    };

    public static bool ShowStats { get; private set; } = true;
    public static string ChatLook { get; private set; } = "";
    public static string ChatColors { get; private set; } = "";
    public static int CombatLogBackground { get; private set; } = LibreKO.Domain.ChatPrefs.DefaultBackground;
    public static bool CombatLogLocked { get; private set; }

    public const float CamTurnSpeedMin = 90f;
    public const float CamTurnSpeedMax = 1080f;
    public const float CamEdgePanSpeedMin = 20f;
    public const float CamEdgePanSpeedMax = 300f;
    public const float CamEdgePanSpeedRetail = 114.6f;
    public static float CamTurnSpeed { get; private set; } = 360f;
    public static bool CamEdgePan { get; private set; } = true;
    public static bool CameraShake { get; private set; } = true;
    public static bool DestroyWellLocked { get; private set; }
    public static WhisperSize WhisperSize { get; private set; } = WhisperLook.DefaultSize;
    public static float WhisperAlpha { get; private set; } = WhisperLook.DefaultAlpha;
    public static byte PartyRowMeter { get; private set; } = PartyWarBoard.ModeOff;
    public static byte PartyBoardMode { get; private set; } = PartyWarBoard.ModeOff;
    public static float PartyBoardAlpha { get; private set; } = 1f;
    public static event System.Action? WhisperLookChanged;
    public static float CamEdgePanSpeed { get; private set; } = CamEdgePanSpeedRetail;


    public static bool FxAmbient { get; private set; } = true;
    public static int FxDistance { get; private set; } = 180;

    public const float AnimFullDistMin = 5f;
    public const float AnimFullDistMax = 160f;
    public const int AnimThrottleHzMin = 1;
    public const int AnimThrottleHzMax = 60;
    public static bool EntityAnim { get; private set; } = true;
    public static float AnimFullDist { get; private set; } = 25f;
    public static float AnimMidDist { get; private set; } = 60f;
    public static int AnimMidHz { get; private set; } = 20;
    public static int AnimThrottleHz { get; private set; } = 10;
    public const int AnimCountMin = 1;
    public const int AnimCountMax = 2000;
    public static int AnimFullCount { get; private set; } = 48;
    public static int AnimMidCount { get; private set; } = 96;
    public const int AnimEveryMin = 1;
    public const int AnimEveryMax = 16;
    public static int AnimMidEvery { get; private set; } = 2;
    public static int AnimFarEvery { get; private set; } = 4;
    public static int MoveFarEvery { get; private set; } = 2;
    public const int ShineLightBudgetMax = 500;
    public static int ShineLightBudget { get; private set; } = 12;
    public static int LampLightBudget { get; private set; } = 24;
    public static bool MergeCharacters { get; private set; } = true;
    public static bool DamageNumbers { get; private set; } = true;
    public static bool CombatLog { get; private set; } = false;

    public static bool AudioEnabled { get; private set; } = true;
    public static bool AudioMuted { get; private set; } = false;
    public static float MasterVolume { get; private set; } = 0.8f;
    public static float MusicVolume { get; private set; } = 0.5f;
    public static float SfxVolume { get; private set; } = 0.9f;
    public static float UiVolume { get; private set; } = 0.7f;
    public static float VoiceVolume { get; private set; } = 0.9f;
    public static float VoiceChatVolume { get; private set; } = VoiceLevels.DefaultVolume;
    public static float VoiceThreshold { get; private set; } = 0.35f;
    public static bool VoicePushToTalk { get; private set; }
    public static string VoiceInputDevice { get; private set; } = DefaultAudioDevice;
    public const string DefaultAudioDevice = "Default";
    public static bool OtherPlayersSounds { get; private set; } = true;
    public static int DailyRankLastViewDay { get; private set; }
    public static int TimedItemsHiddenDay { get; private set; }
    public static int ChaosGuideHiddenDay { get; private set; }
    public static int FullMoonCaptionHiddenDay { get; private set; }
    public static bool QuestTrackerCollapsed { get; private set; }
    public const float EffectSoundDistanceMin = 20f;
    public const float EffectSoundDistanceMax = 48f;
    public static float EffectSoundDistance { get; private set; } = EffectSoundDistanceMax;

    public static bool HotbarLocked { get; private set; } = false;
    public static bool HotbarVertical { get; private set; } = false;
    public static int HotbarExtraBars { get; private set; } = 0;
    public static float HotbarScale { get; private set; } = HotbarLayout.ScaleDefault;
    public static int HotbarGap { get; private set; } = HotbarLayout.GapDefault;
    public static event System.Action? HotbarLookChanged;
    private static readonly int[] _hotbarExtraPages = System.Linq.Enumerable.Range(1, HotbarLayout.MaxBars - 1).ToArray();

    public static int HotbarExtraPage(int extraBar) =>
        extraBar >= 0 && extraBar < _hotbarExtraPages.Length ? _hotbarExtraPages[extraBar] : 0;

    public static int TooltipHeight { get; private set; } = 11;
    public static bool TooltipBold { get; private set; } = false;
    public static bool TooltipBack { get; private set; } = false;
    private static readonly Color[] DefaultTooltipColors =
    {
        Argb(0xFFFFFFFF), Argb(0xFFADFF2F), Argb(0xFFF15F5F), Argb(0xFF5CD1E5),
        Argb(0xFF86E57F), Argb(0xFF1E90FF), Argb(0xFFADFF2F), Argb(0xFFCDC300),
        Argb(0xFFEAF50C), Argb(0xFFFFFF00), Argb(0xFFFFFF00), Argb(0xFF7FFF00),
        Argb(0xFFFFFFFF), Argb(0xFFFF0000),
        Argb(0xFFFFFFFF), Argb(0xFFD68CFF), Argb(0xFFFFCC33), Argb(0xFFFF66CC), Argb(0xFFFF9933),
        Argb(0xFF87CEEB), Argb(0xFFFFFF00), Argb(0xFF7FFF00), Argb(0xFF00BFFF), Argb(0xFF00FFCC),
    };

    public const int RarityNameRegular = 14;
    public const int RarityNameUpgrade = 15;
    public const int RarityNameUnique = 16;
    public const int RarityNameReverse = 17;
    public const int RarityNameReverseUnique = 18;
    public const int RarityNameMagic = 19;
    public const int RarityNameRare = 20;
    public const int RarityNameCraft = 21;
    public const int RarityNameEvent = 22;
    public const int RarityNameCospre = 23;
    private static Color[] _tooltipColors = (Color[])DefaultTooltipColors.Clone();

    private static T ReadEnum<T>(ConfigFile cfg, string section, string key, T fallback)
        where T : struct, System.Enum
    {
        int raw = cfg.GetValue(section, key, System.Convert.ToInt32(fallback)).AsInt32();
        var parsed = (T)System.Enum.ToObject(typeof(T), raw);
        return System.Enum.IsDefined(parsed) ? parsed : fallback;
    }

    public static int FpsValue(FpsCap c) => c switch
    {
        FpsCap.Fps30 => 30, FpsCap.Fps60 => 60, FpsCap.Fps120 => 120,
        FpsCap.Fps144 => 144, FpsCap.Fps240 => 240, _ => 0,
    };

    public static event System.Action? GraphicsChanged;

    public static event System.Action? AudioChanged;
    public static event System.Action? VoiceChatChanged;

    public static event System.Action? EffectsChanged;

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        ReadLayer(DefaultsResPath, server: true, shipped: true);

        string installPath = Path.Combine(InstallDir(), SettingsFileName);
        bool haveInstall = ReadLayer(installPath, server: true);

        InstallDirWritable = DirIsWritable(InstallDir());
        SettingsSavePath = InstallDirWritable ? installPath : Path.Combine(UserDir(), SettingsFileName);

        if (!InstallDirWritable)
            ReadLayer(SettingsSavePath, server: false);

        GD.Print($"[config] host={ServerHost}:{ServerPort} game={GamePort} v={ServerVersion} dev={Development}");
        GD.Print($"[config] source={(haveInstall ? installPath : "shipped defaults")} save={SettingsSavePath}");
        GD.Print($"[config] uiScale={UiScale:0.00} viewDistance={ViewDistance:0.00} "
                 + $"touchUi={Platform.TouchUi} mode={TouchUi} screen={Platform.HasTouchscreen}");
        if (!InstallDirWritable)
            GD.PushWarning($"[config] '{InstallDir()}' is not writable — preferences go to {SettingsSavePath}. " +
                           "Server settings still come from the install directory.");

        if ((!haveInstall || _hostMigrated) && InstallDirWritable)
            Save();

        ApplyCommandLineOverrides();
        ApplyGraphicsToViewport();
        ApplyUiScale();
        SavedAccount.Load();
    }

    private static ConfigFile? _shippedDefaults;
    private static bool _hostMigrated;
    private static string _retiredHost = "";

    public static void PinShippedDefaults()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(DefaultsResPath) == Error.Ok) _shippedDefaults = cfg;
    }

    private static bool ReadLayer(string path, bool server, bool shipped = false)
    {
        var cfg = shipped ? _shippedDefaults : null;
        if (cfg == null)
        {
            cfg = new ConfigFile();
            if (cfg.Load(path) != Error.Ok)
                return false;
        }

        if (server)
        {
            string host = cfg.GetValue("server", "host", ServerHost).AsString();
            if (shipped) _retiredHost = cfg.GetValue("server", "retired_host", "").AsString();
            ServerHost = shipped ? host : ServerHosts.Migrate(host, _retiredHost, ServerHost);
            if (!shipped && ServerHost != host) _hostMigrated = true;
            ServerPort = cfg.GetValue("server", "login_port", ServerPort).AsInt32();
            GamePort = cfg.GetValue("server", "game_port", GamePort).AsInt32();
            ServerVersion = cfg.GetValue("server", "version", ServerVersion).AsInt32();
            PingEnabled = cfg.GetValue("server", "ping_enabled", PingEnabled).AsBool();
            Development = cfg.GetValue("server", "development", Development).AsBool();
        }
        PatchUrl = cfg.GetValue("patch", "url", PatchUrl).AsString();
        LegacyQuestFallback = cfg.GetValue("quests", "legacy_fallback", LegacyQuestFallback).AsBool();
        if (cfg.HasSection("plugins"))
        {
            foreach (string key in cfg.GetSectionKeys("plugins"))
            {
                if (key == "dirs") PluginDirs = cfg.GetValue("plugins", key, PluginDirs).AsString();
                else _pluginEnabled[key] = cfg.GetValue("plugins", key, false).AsBool();
            }
        }
        WindowMode = ReadEnum(cfg, "video", "mode", WindowMode);
        Language = ReadEnum(cfg, "game", "language", Language);
        WinWidth = cfg.GetValue("video", "width", WinWidth).AsInt32();
        WinHeight = cfg.GetValue("video", "height", WinHeight).AsInt32();
        UiScaleChosen = cfg.HasSectionKey("video", "ui_scale");
        UiScale = Mathf.Clamp((float)cfg.GetValue("video", "ui_scale",
                              Platform.TouchUi ? UiScaleTouchDefault : UiScale).AsDouble(),
                              UiScaleMin, UiScaleMax);
        NamePlateScale = Mathf.Clamp((float)cfg.GetValue("video", "name_plate_scale", NamePlateScale).AsDouble(),
                                     NamePlateScaleMin, NamePlateScaleMax);
        MoveStickSensitivity = Mathf.Clamp(
            (float)cfg.GetValue("controls", "move_stick", MoveStickSensitivity).AsDouble(),
            StickSensitivityMin, StickSensitivityMax);
        LookStickSensitivity = Mathf.Clamp(
            (float)cfg.GetValue("controls", "look_stick", LookStickSensitivity).AsDouble(),
            StickSensitivityMin, StickSensitivityMax);
        ShadowQuality = ReadLevel(cfg, "shadow_quality", "shadows", ShadowQuality);
        SsaoQuality = ReadLevel(cfg, "ssao_quality", "ssao", SsaoQuality);
        Renderer = ReadEnum(cfg, "graphics", "renderer", Renderer);
        Screen = cfg.GetValue("video", "screen", Screen).AsInt32();
        ShowTitles = cfg.GetValue("hud", "show_titles", ShowTitles).AsBool();
        QuestNotices = cfg.GetValue("hud", "quest_notices", QuestNotices).AsBool();
        UiAlpha = LibreKO.Domain.UiOpacityRules.Clamp(cfg.GetValue("hud", "ui_alpha", UiAlpha).AsInt32());
        HiddenCospre = (CospreKinds)cfg.GetValue("graphics", "hidden_cospre", (int)HiddenCospre).AsInt32();
        VolumetricFog = cfg.GetValue("graphics", "volumetric_fog", VolumetricFog).AsBool();
        Bloom = cfg.GetValue("graphics", "bloom", Bloom).AsBool();
        FxLayer = cfg.GetValue("graphics", "fx_layer", FxLayer).AsBool();
        Crowd = ReadEnum(cfg, "graphics", "crowd_mode", Crowd);
        Shine = ReadEnum(cfg, "effects", "item_shine", Shine);
        Clouds = cfg.GetValue("graphics", "clouds", Clouds).AsBool();
        Capes = cfg.GetValue("graphics", "capes", Capes).AsBool();
        AntiAlias = ReadEnum(cfg, "graphics", "aa", AntiAlias);
        Upscale = ReadEnum(cfg, "graphics", "upscale", Upscale);
        UpscaleQuality = ReadEnum(cfg, "graphics", "upscale_quality", UpscaleQuality);
        VSync = cfg.GetValue("video", "vsync", VSync).AsBool();
        if (shipped && Platform.TouchUi) FpsLimit = FpsMobileDefault;
        else FpsLimit = ReadEnum(cfg, "graphics", "fps_limit", FpsLimit);
        ShowStats = cfg.GetValue("hud", "show_stats", ShowStats).AsBool();
        ChatLook = cfg.GetValue("hud", "chat_look", ChatLook).AsString();
        ChatColors = cfg.GetValue("hud", "chat_colors", ChatColors).AsString();
        CombatLogBackground = Mathf.Clamp(cfg.GetValue("hud", "combat_log_bg", CombatLogBackground).AsInt32(),
            0, LibreKO.Domain.ChatPrefs.Backgrounds.Length - 1);
        CombatLogLocked = cfg.GetValue("hud", "combat_log_lock", CombatLogLocked).AsBool();
        CamTurnSpeed = Mathf.Clamp((float)cfg.GetValue("controls", "cam_turn_speed", CamTurnSpeed).AsDouble(),
            CamTurnSpeedMin, CamTurnSpeedMax);
        CamEdgePan = cfg.GetValue("controls", "cam_edge_pan", CamEdgePan).AsBool();
        CameraShake = cfg.GetValue("controls", "camera_shake", CameraShake).AsBool();
        DestroyWellLocked = cfg.GetValue("inventory", "destroy_locked", DestroyWellLocked).AsBool();
        WhisperSize = WhisperLook.SizeFrom(cfg.GetValue("whisper", "size", (int)WhisperSize).AsInt32());
        WhisperAlpha = WhisperLook.ClampAlpha((float)cfg.GetValue("whisper", "alpha", WhisperAlpha).AsDouble());
        PartyRowMeter = PartyWarBoard.SavedMode(cfg.GetValue("party", "row_meter", (int)PartyRowMeter).AsInt32(), PartyRowMeter);
        PartyBoardMode = PartyWarBoard.SavedMode(cfg.GetValue("party", "board_mode", (int)PartyBoardMode).AsInt32(), PartyBoardMode);
        PartyBoardAlpha = PartyWarBoard.SavedAlpha((float)cfg.GetValue("party", "board_alpha", PartyBoardAlpha).AsDouble());
        CamEdgePanSpeed = Mathf.Clamp((float)cfg.GetValue("controls", "cam_edge_pan_speed", CamEdgePanSpeed).AsDouble(),
            CamEdgePanSpeedMin, CamEdgePanSpeedMax);
        FxAmbient = cfg.GetValue("effects", "ambient", FxAmbient).AsBool();
        FxDistance = Mathf.Clamp(cfg.GetValue("effects", "distance", FxDistance).AsInt32(), 40, 400);
        EntityAnim = cfg.GetValue("debug", "entity_anim", EntityAnim).AsBool();
        AnimFullDist = Mathf.Clamp((float)cfg.GetValue("debug", "anim_full_dist", AnimFullDist).AsDouble(),
            AnimFullDistMin, AnimFullDistMax);
        AnimThrottleHz = Mathf.Clamp(cfg.GetValue("debug", "anim_throttle_hz", AnimThrottleHz).AsInt32(),
            AnimThrottleHzMin, AnimThrottleHzMax);
        AnimMidDist = Mathf.Clamp((float)cfg.GetValue("debug", "anim_mid_dist", AnimMidDist).AsDouble(),
            AnimFullDistMin, AnimFullDistMax);
        AnimMidHz = Mathf.Clamp(cfg.GetValue("debug", "anim_mid_hz", AnimMidHz).AsInt32(),
            AnimThrottleHzMin, AnimThrottleHzMax);
        AnimFullCount = Mathf.Clamp(cfg.GetValue("debug", "anim_full_count", AnimFullCount).AsInt32(),
            AnimCountMin, AnimCountMax);
        AnimMidCount = Mathf.Clamp(cfg.GetValue("debug", "anim_mid_count", AnimMidCount).AsInt32(),
            AnimCountMin, AnimCountMax);
        AnimMidEvery = Mathf.Clamp(cfg.GetValue("debug", "anim_mid_every", AnimMidEvery).AsInt32(),
            AnimEveryMin, AnimEveryMax);
        AnimFarEvery = Mathf.Clamp(cfg.GetValue("debug", "anim_far_every", AnimFarEvery).AsInt32(),
            AnimEveryMin, AnimEveryMax);
        MoveFarEvery = Mathf.Clamp(cfg.GetValue("debug", "move_far_every", MoveFarEvery).AsInt32(),
            AnimEveryMin, AnimEveryMax);
        ShineLightBudget = Mathf.Clamp(cfg.GetValue("debug", "shine_light_budget", ShineLightBudget).AsInt32(),
            0, ShineLightBudgetMax);
        LampLightBudget = Mathf.Clamp(cfg.GetValue("debug", "lamp_light_budget", LampLightBudget).AsInt32(),
            0, ShineLightBudgetMax);
        MergeCharacters = cfg.GetValue("debug", "merge_characters", MergeCharacters).AsBool();
        TouchUi = ReadEnum(cfg, "video", "touch_ui", TouchUi);
        ViewDistance = Mathf.Clamp((float)cfg.GetValue("graphics", "view_distance",
            Platform.TouchUi ? ViewDistanceMobileDefault : ViewDistance).AsDouble(),
            ViewDistanceMin, ViewDistanceMax);
        Brightness = LightSliders.Brightness.Clamp(cfg.GetValue("graphics", "brightness", Brightness).AsInt32());
        Contrast = LightSliders.Contrast.Clamp(cfg.GetValue("graphics", "contrast", Contrast).AsInt32());
        NightBrightness = LightSliders.NightBrightness.Clamp(cfg.GetValue("graphics", "night_brightness", NightBrightness).AsInt32());
        Fog = LightSliders.Fog.Clamp(cfg.GetValue("graphics", "fog", Fog).AsInt32());
        DamageNumbers = cfg.GetValue("effects", "damage_numbers", DamageNumbers).AsBool();
        CombatLog = cfg.GetValue("hud", "combat_log", CombatLog).AsBool();
        AudioEnabled = cfg.GetValue("audio", "enabled", AudioEnabled).AsBool();
        AudioMuted = cfg.GetValue("audio", "muted", AudioMuted).AsBool();
        MasterVolume = Vol(cfg, "master", MasterVolume);
        MusicVolume = Vol(cfg, "music", MusicVolume);
        SfxVolume = Vol(cfg, "sfx", SfxVolume);
        UiVolume = Vol(cfg, "ui", UiVolume);
        VoiceVolume = Vol(cfg, "voice", VoiceVolume);
        VoiceChatVolume = Mathf.Clamp((float)cfg.GetValue("voice_chat", "volume", VoiceChatVolume).AsDouble(), 0f, VoiceLevels.MaxVolume);
        VoiceThreshold = Mathf.Clamp((float)cfg.GetValue("voice_chat", "threshold", VoiceThreshold).AsDouble(), 0f, 1f);
        VoicePushToTalk = cfg.GetValue("voice_chat", "push_to_talk", VoicePushToTalk).AsBool();
        VoiceInputDevice = cfg.GetValue("voice_chat", "input_device", VoiceInputDevice).AsString();
        OtherPlayersSounds = cfg.GetValue("audio", "other_players", OtherPlayersSounds).AsBool();
        DailyRankLastViewDay = cfg.GetValue("dailyrank", "last_view_day", DailyRankLastViewDay).AsInt32();
        TimedItemsHiddenDay = cfg.GetValue("itemlist", "item_time", TimedItemsHiddenDay).AsInt32();
        ChaosGuideHiddenDay = cfg.GetValue("chaos", "view_caption", ChaosGuideHiddenDay).AsInt32();
        FullMoonCaptionHiddenDay = cfg.GetValue("fullmoon", "view_caption", FullMoonCaptionHiddenDay).AsInt32();
        QuestTrackerCollapsed = cfg.GetValue("quest", "tracker_collapsed", QuestTrackerCollapsed).AsBool();
        EffectSoundDistance = Mathf.Clamp((float)cfg.GetValue("audio", "effect_distance", EffectSoundDistance).AsDouble(),
            EffectSoundDistanceMin, EffectSoundDistanceMax);
        HotbarLocked = cfg.GetValue("hotbar", "locked", HotbarLocked).AsBool();
        HotbarVertical = cfg.GetValue("hotbar", "vertical", HotbarVertical).AsBool();
        HotbarScale = HotbarLayout.ClampScale((float)cfg.GetValue("hotbar", "scale", HotbarScale).AsDouble());
        HotbarGap = HotbarLayout.ClampGap(cfg.GetValue("hotbar", "gap", HotbarGap).AsInt32());
        HotbarExtraBars = Mathf.Clamp(cfg.GetValue("hotbar", "extra_bars", HotbarExtraBars).AsInt32(),
                                      0, HotbarLayout.MaxBars - 1);
        for (int i = 0; i < _hotbarExtraPages.Length; i++)
            _hotbarExtraPages[i] = Mathf.Clamp(cfg.GetValue("hotbar", $"extra_page_{i}", _hotbarExtraPages[i]).AsInt32(),
                                               0, HotbarLayout.Pages - 1);
        TooltipHeight = Mathf.Max(1, cfg.GetValue("fontstate", "tooltipheight", TooltipHeight).AsInt32());
        TooltipBold = cfg.GetValue("fontstate", "tooltipbold", TooltipBold).AsBool();
        TooltipBack = cfg.GetValue("fontstate", "tooltipback", TooltipBack).AsBool();
        for (int i = 0; i < _tooltipColors.Length; i++)
            _tooltipColors[i] = ReadTooltipColor(cfg, i);
        RestoreRotatedTooltipColors();
        return true;
    }

    public static void Save()
    {
        if (SettingsSavePath.Length == 0)
            SettingsSavePath = Path.Combine(InstallDir(), SettingsFileName);

        var cfg = new ConfigFile();
        cfg.Load(SettingsSavePath);

        cfg.SetValue("server", "host", ServerHost);
        cfg.SetValue("server", "login_port", ServerPort);
        cfg.SetValue("server", "game_port", GamePort);
        cfg.SetValue("server", "version", ServerVersion);
        cfg.SetValue("server", "ping_enabled", PingEnabled);
        cfg.SetValue("server", "development", Development);
        cfg.SetValue("quests", "legacy_fallback", LegacyQuestFallback);
        if (PluginDirs.Length > 0) cfg.SetValue("plugins", "dirs", PluginDirs);
        foreach (var (id, on) in _pluginEnabled) cfg.SetValue("plugins", id, on);
        cfg.SetValue("video", "mode", (int)WindowMode);
        cfg.SetValue("video", "width", WinWidth);
        cfg.SetValue("video", "height", WinHeight);
        if (UiScaleChosen) cfg.SetValue("video", "ui_scale", UiScale);
        else if (cfg.HasSectionKey("video", "ui_scale")) cfg.EraseSectionKey("video", "ui_scale");
        cfg.SetValue("video", "name_plate_scale", NamePlateScale);
        cfg.SetValue("game", "language", (int)Language);
        cfg.SetValue("video", "touch_ui", (int)TouchUi);
        cfg.SetValue("graphics", "shadow_quality", (int)ShadowQuality);
        cfg.SetValue("graphics", "ssao_quality", (int)SsaoQuality);
        cfg.SetValue("graphics", "renderer", (int)Renderer);
        cfg.SetValue("video", "screen", Screen);
        cfg.SetValue("hud", "show_titles", ShowTitles);
        cfg.SetValue("hud", "quest_notices", QuestNotices);
        cfg.SetValue("hud", "ui_alpha", UiAlpha);
        cfg.SetValue("graphics", "hidden_cospre", (int)HiddenCospre);
        cfg.SetValue("graphics", "volumetric_fog", VolumetricFog);
        cfg.SetValue("graphics", "bloom", Bloom);
        cfg.SetValue("graphics", "fx_layer", FxLayer);
        cfg.SetValue("graphics", "crowd_mode", (int)Crowd);
        cfg.SetValue("effects", "item_shine", (int)Shine);
        cfg.SetValue("graphics", "clouds", Clouds);
        cfg.SetValue("graphics", "capes", Capes);
        cfg.SetValue("graphics", "aa", (int)AntiAlias);
        cfg.SetValue("graphics", "upscale", (int)Upscale);
        cfg.SetValue("graphics", "upscale_quality", (int)UpscaleQuality);
        cfg.SetValue("video", "vsync", VSync);
        cfg.SetValue("graphics", "fps_limit", (int)FpsLimit);
        cfg.SetValue("hud", "show_stats", ShowStats);
        cfg.SetValue("hud", "chat_look", ChatLook);
        cfg.SetValue("hud", "chat_colors", ChatColors);
        cfg.SetValue("hud", "combat_log_bg", CombatLogBackground);
        cfg.SetValue("hud", "combat_log_lock", CombatLogLocked);
        cfg.SetValue("hotbar", "locked", HotbarLocked);
        cfg.SetValue("hotbar", "vertical", HotbarVertical);
        cfg.SetValue("hotbar", "scale", HotbarScale);
        cfg.SetValue("hotbar", "gap", HotbarGap);
        cfg.SetValue("hotbar", "extra_bars", HotbarExtraBars);
        for (int i = 0; i < _hotbarExtraPages.Length; i++)
            cfg.SetValue("hotbar", $"extra_page_{i}", _hotbarExtraPages[i]);
        cfg.SetValue("controls", "move_stick", MoveStickSensitivity);
        cfg.SetValue("controls", "look_stick", LookStickSensitivity);
        cfg.SetValue("controls", "cam_turn_speed", CamTurnSpeed);
        cfg.SetValue("controls", "cam_edge_pan", CamEdgePan);
        cfg.SetValue("controls", "camera_shake", CameraShake);
        cfg.SetValue("inventory", "destroy_locked", DestroyWellLocked);
        cfg.SetValue("whisper", "size", (int)WhisperSize);
        cfg.SetValue("whisper", "alpha", WhisperAlpha);
        cfg.SetValue("party", "row_meter", (int)PartyRowMeter);
        cfg.SetValue("party", "board_mode", (int)PartyBoardMode);
        cfg.SetValue("party", "board_alpha", PartyBoardAlpha);
        cfg.SetValue("controls", "cam_edge_pan_speed", CamEdgePanSpeed);
        cfg.SetValue("effects", "ambient", FxAmbient);
        cfg.SetValue("effects", "distance", FxDistance);
        cfg.SetValue("graphics", "view_distance", ViewDistance);
        cfg.SetValue("graphics", "brightness", Brightness);
        cfg.SetValue("graphics", "contrast", Contrast);
        cfg.SetValue("graphics", "night_brightness", NightBrightness);
        cfg.SetValue("graphics", "fog", Fog);
        cfg.SetValue("effects", "damage_numbers", DamageNumbers);
        cfg.SetValue("hud", "combat_log", CombatLog);
        cfg.SetValue("audio", "enabled", AudioEnabled);
        cfg.SetValue("audio", "muted", AudioMuted);
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ui", UiVolume);
        cfg.SetValue("audio", "voice", VoiceVolume);
        cfg.SetValue("voice_chat", "volume", VoiceChatVolume);
        cfg.SetValue("voice_chat", "threshold", VoiceThreshold);
        cfg.SetValue("voice_chat", "push_to_talk", VoicePushToTalk);
        cfg.SetValue("voice_chat", "input_device", VoiceInputDevice);
        cfg.SetValue("audio", "other_players", OtherPlayersSounds);
        cfg.SetValue("dailyrank", "last_view_day", DailyRankLastViewDay);
        cfg.SetValue("itemlist", "item_time", TimedItemsHiddenDay);
        cfg.SetValue("chaos", "view_caption", ChaosGuideHiddenDay);
        cfg.SetValue("fullmoon", "view_caption", FullMoonCaptionHiddenDay);
        cfg.SetValue("quest", "tracker_collapsed", QuestTrackerCollapsed);
        cfg.SetValue("audio", "effect_distance", EffectSoundDistance);
        cfg.SetValue("fontstate", "tooltipheight", TooltipHeight);
        cfg.SetValue("fontstate", "tooltipbold", TooltipBold);
        cfg.SetValue("fontstate", "tooltipback", TooltipBack);
        for (int i = 0; i < _tooltipColors.Length; i++)
            cfg.SetValue("fontstate", $"tooltipcolor{i}", ArgbString(_tooltipColors[i]));

        var err = cfg.Save(SettingsSavePath);
        if (err != Error.Ok)
            GD.PushError($"[config] could not write {SettingsSavePath}: {err}");
    }

    public static void SetVideo(VideoMode mode, int width, int height, bool vsync)
    {
        WindowMode = mode;
        WinWidth = Mathf.Max(640, width);
        WinHeight = Mathf.Max(480, height);
        VSync = vsync;
        ApplyVideo();
        if (DisplayServer.GetName() != "headless") ApplyFrameCap();
        Save();
    }

    public static void SetGraphics(QualityLevel shadows, QualityLevel ssao, bool volumetricFog, bool bloom, bool clouds,
                                   bool capes, AaMode aa, UpscaleMode upscale, UpscaleLevel quality,
                                   FpsCap fps)
    {
        Upscale = upscale;
        UpscaleQuality = quality;
        ShadowQuality = shadows;
        SsaoQuality = ssao;
        VolumetricFog = volumetricFog;
        Bloom = bloom;
        Clouds = clouds;
        Capes = capes;
        AntiAlias = aa;
        FpsLimit = fps;
        ApplyGraphicsToViewport();
        Save();
        GraphicsChanged?.Invoke();
    }

    public static void SetUiAlpha(int alpha)
    {
        alpha = LibreKO.Domain.UiOpacityRules.Clamp(alpha);
        if (UiAlpha == alpha) return;
        UiAlpha = alpha;
        Save();
        UiAlphaChanged?.Invoke();
    }

    public static void SetQuestNotices(bool on)
    {
        if (QuestNotices == on) return;
        QuestNotices = on;
        Save();
    }

    public static void SetShowTitles(bool show)
    {
        if (ShowTitles == show) return;
        ShowTitles = show;
        Save();
        PlatesChanged?.Invoke();
    }

    public static void SetHiddenCospre(CospreKinds hidden)
    {
        if (HiddenCospre == hidden) return;
        HiddenCospre = hidden;
        Save();
        CospreChanged?.Invoke();
    }

    public static void SetScreen(int screen)
    {
        Screen = screen >= 0 && screen < DisplayServer.GetScreenCount() ? screen : CurrentScreen;
        Save();
    }

    private const string OverrideFileName = "override.cfg";
    private const string OverrideSection = "rendering";
    private const string OverrideDriverKey = "rendering_device/driver.windows";
    private const string DriverD3D12 = "d3d12";
    private const string DriverVulkan = "vulkan";

    public static string RunningDriver => RenderingServer.GetCurrentRenderingDriverName();

    public static bool SetRenderer(RenderDriver driver)
    {
        if (Renderer == driver) return false;
        Renderer = driver;
        Save();
        var path = Path.Combine(InstallDir(), OverrideFileName);
        var cfg = new ConfigFile();
        cfg.Load(path);
        cfg.SetValue(OverrideSection, OverrideDriverKey, driver == RenderDriver.Vulkan ? DriverVulkan : DriverD3D12);
        return cfg.Save(path) == Error.Ok;
    }

    public static bool RendererRunning(RenderDriver driver) =>
        RunningDriver == (driver == RenderDriver.Vulkan ? DriverVulkan : DriverD3D12);

    private static QualityLevel ReadLevel(ConfigFile cfg, string key, string oldSwitchKey, QualityLevel fallback)
    {
        if (cfg.HasSectionKey("graphics", key)) return ReadEnum(cfg, "graphics", key, fallback);
        if (cfg.HasSectionKey("graphics", oldSwitchKey))
            return cfg.GetValue("graphics", oldSwitchKey, true).AsBool() ? fallback : QualityLevel.Off;
        return fallback;
    }

    private static readonly System.Collections.Generic.Dictionary<string, object?> CompiledDefaults = new();

    static Config()
    {
        foreach (var property in typeof(Config).GetProperties(
                     System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            if (property.CanWrite || property.SetMethod != null)
                CompiledDefaults[property.Name] = property.GetValue(null);
    }

    public static T DefaultOf<T>(string property, string section, string key)
    {
        var shipped = new ConfigFile();
        object? compiled = CompiledDefaults.TryGetValue(property, out var value) ? value : default(T);
        if (shipped.Load(DefaultsResPath) != Error.Ok || !shipped.HasSectionKey(section, key))
            return compiled is T typed ? typed : default!;
        var raw = shipped.GetValue(section, key);
        object? converted = typeof(T) switch
        {
            var t when t == typeof(bool) => raw.AsBool(),
            var t when t == typeof(float) => (float)raw.AsDouble(),
            var t when t == typeof(int) => raw.AsInt32(),
            var t when t.IsEnum => System.Enum.ToObject(t, raw.AsInt32()),
            _ => compiled,
        };
        return converted is T result ? result : default!;
    }

    public static void SuspendFxLayer()
    {
        if (!FxLayer) return;
        FxLayer = false;
        GraphicsChanged?.Invoke();
    }

    public static void SetShineStyle(LibreKO.Domain.ShineStyle style)
    {
        if (Shine == style) return;
        Shine = style;
        Save();
        ItemShine.ApplyStyle();
    }

    public static void SetCrowdMode(LibreKO.Domain.CrowdMode mode)
    {
        if (Crowd == mode) return;
        Crowd = mode;
        Save();
    }

    public static void SetFxLayer(bool on)
    {
        if (FxLayer == on) return;
        FxLayer = on;
        Save();
        GraphicsChanged?.Invoke();
    }

    private static float Vol(ConfigFile cfg, string key, float fallback) =>
        Mathf.Clamp((float)cfg.GetValue("audio", key, fallback).AsDouble(), 0f, 1f);

    public static void SetAudio(bool enabled, bool muted, float master, float music,
                                float sfx, float ui, float voice)
    {
        AudioEnabled = enabled;
        AudioMuted = muted;
        MasterVolume = Mathf.Clamp(master, 0f, 1f);
        MusicVolume = Mathf.Clamp(music, 0f, 1f);
        SfxVolume = Mathf.Clamp(sfx, 0f, 1f);
        UiVolume = Mathf.Clamp(ui, 0f, 1f);
        VoiceVolume = Mathf.Clamp(voice, 0f, 1f);
        Save();
        AudioChanged?.Invoke();
    }

    public static void SetVoiceChat(float volume, float threshold, bool pushToTalk, string inputDevice)
    {
        VoiceChatVolume = Mathf.Clamp(volume, 0f, VoiceLevels.MaxVolume);
        VoiceThreshold = Mathf.Clamp(threshold, 0f, 1f);
        VoicePushToTalk = pushToTalk;
        VoiceInputDevice = inputDevice.Length == 0 ? DefaultAudioDevice : inputDevice;
        Save();
        VoiceChatChanged?.Invoke();
    }

    public static void SetDailyRankLastViewDay(int dayKey)
    {
        if (DailyRankLastViewDay == dayKey) return;
        DailyRankLastViewDay = dayKey;
        Save();
    }

    public static void SetQuestTrackerCollapsed(bool collapsed)
    {
        if (QuestTrackerCollapsed == collapsed) return;
        QuestTrackerCollapsed = collapsed;
        Save();
    }

    public static void SetChaosGuideHiddenDay(int dayKey)
    {
        if (ChaosGuideHiddenDay == dayKey) return;
        ChaosGuideHiddenDay = dayKey;
        Save();
    }

    public static void SetFullMoonCaptionHiddenDay(int dayKey)
    {
        if (FullMoonCaptionHiddenDay == dayKey) return;
        FullMoonCaptionHiddenDay = dayKey;
        Save();
    }

    public static void SetTimedItemsHiddenDay(int dayKey)
    {
        if (TimedItemsHiddenDay == dayKey) return;
        TimedItemsHiddenDay = dayKey;
        Save();
    }

    public static void SetOtherPlayersSounds(bool on)
    {
        if (OtherPlayersSounds == on) return;
        OtherPlayersSounds = on;
        Save();
    }

    public static void SetEffects(bool ambient, int distance, bool damageNumbers, bool combatLog)
    {
        FxAmbient = ambient;
        FxDistance = Mathf.Clamp(distance, 40, 400);
        DamageNumbers = damageNumbers;
        CombatLog = combatLog;
        Save();
        EffectsChanged?.Invoke();
    }

    public static void SetChatLook(string look)
    {
        if (ChatLook == look) return;
        ChatLook = look;
        Save();
    }

    public static void SetChatColors(string colors)
    {
        if (ChatColors == colors) return;
        ChatColors = colors;
        Save();
    }

    public static void SetCombatLogBackground(int level)
    {
        if (CombatLogBackground == level) return;
        CombatLogBackground = level;
        Save();
    }

    public static void SetCombatLogLocked(bool locked)
    {
        if (CombatLogLocked == locked) return;
        CombatLogLocked = locked;
        Save();
    }

    public static void SetShowStats(bool show)
    {
        if (ShowStats == show) return;
        ShowStats = show;
        Save();
    }

    public static void SetHotbarLayout(bool locked, bool vertical, int extraBars)
    {
        HotbarLocked = locked;
        HotbarVertical = vertical;
        HotbarExtraBars = Mathf.Clamp(extraBars, 0, HotbarLayout.MaxBars - 1);
        Save();
    }

    public static void SetWhisperLook(WhisperSize size, float alpha)
    {
        float clamped = WhisperLook.ClampAlpha(alpha);
        if (size == WhisperSize && Mathf.IsEqualApprox(clamped, WhisperAlpha)) return;
        WhisperSize = size;
        WhisperAlpha = clamped;
        Save();
        WhisperLookChanged?.Invoke();
    }

    public static void SetPartyRowMeter(byte mode)
    {
        if (mode == PartyRowMeter) return;
        PartyRowMeter = mode;
        Save();
    }

    public static void SetPartyBoardMode(byte mode)
    {
        if (mode == PartyBoardMode || mode == PartyWarBoard.ModeOff) return;
        PartyBoardMode = mode;
        Save();
    }

    public static void SetPartyBoardAlpha(float alpha)
    {
        float clamped = PartyWarBoard.SavedAlpha(alpha);
        if (Mathf.IsEqualApprox(clamped, PartyBoardAlpha)) return;
        PartyBoardAlpha = clamped;
        Save();
    }

    public static void SetDestroyWellLocked(bool locked)
    {
        if (locked == DestroyWellLocked) return;
        DestroyWellLocked = locked;
        Save();
    }

    public static void SetCameraShake(bool on)
    {
        if (on == CameraShake) return;
        CameraShake = on;
        Save();
    }

    public static void SetHotbarLook(float scale, int gap)
    {
        float clampedScale = HotbarLayout.ClampScale(scale);
        int clampedGap = HotbarLayout.ClampGap(gap);
        if (Mathf.IsEqualApprox(clampedScale, HotbarScale) && clampedGap == HotbarGap) return;
        HotbarScale = clampedScale;
        HotbarGap = clampedGap;
        Save();
        HotbarLookChanged?.Invoke();
    }

    public static void SetHotbarExtraPage(int extraBar, int page)
    {
        if (extraBar < 0 || extraBar >= _hotbarExtraPages.Length) return;
        _hotbarExtraPages[extraBar] = Mathf.Clamp(page, 0, HotbarLayout.Pages - 1);
        Save();
    }

    public static void SetControls(float camTurnSpeed, bool camEdgePan, float camEdgePanSpeed)
    {
        CamTurnSpeed = Mathf.Clamp(camTurnSpeed, CamTurnSpeedMin, CamTurnSpeedMax);
        CamEdgePan = camEdgePan;
        CamEdgePanSpeed = Mathf.Clamp(camEdgePanSpeed, CamEdgePanSpeedMin, CamEdgePanSpeedMax);
        Save();
    }

    private static void ApplyFrameCap() =>
        Engine.MaxFps = FrameLimit.EngineCap(FpsValue(FpsLimit), VSync,
            DisplayServer.ScreenGetRefreshRate(DisplayServer.WindowGetCurrentScreen()));

    public static void ApplyGraphicsToViewport()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        ApplyFrameCap();
        ApplyQualityLevels();
        if (Engine.GetMainLoop() is not SceneTree { Root: { } vp })
            return;
        var upscale = EffectiveUpscale;
        vp.Scaling3DScale = Upscale == UpscaleMode.Off ? 1f : RenderScale;
        vp.Scaling3DMode = upscale switch
        {
            UpscaleMode.Fsr1 => Viewport.Scaling3DModeEnum.Fsr,
            UpscaleMode.Fsr2 => Viewport.Scaling3DModeEnum.Fsr2,
            _ => Viewport.Scaling3DModeEnum.Bilinear,
        };
        vp.FsrSharpness = FsrSharpness;
        vp.Msaa3D = AntiAlias switch
        {
            AaMode.Msaa2X => Viewport.Msaa.Msaa2X,
            AaMode.Msaa4X => Viewport.Msaa.Msaa4X,
            _ => Viewport.Msaa.Disabled,
        };
        vp.ScreenSpaceAA = AntiAlias == AaMode.Fxaa && upscale != UpscaleMode.Fsr2
            ? Viewport.ScreenSpaceAAEnum.Fxaa
            : Viewport.ScreenSpaceAAEnum.Disabled;
    }

    private const int ShadowAtlasLow = 1024;
    private const int ShadowAtlasMedium = 2048;
    private const int ShadowAtlasHigh = 4096;
    private const float SsaoAdaptiveTarget = 0.5f;
    private const int SsaoBlurPasses = 2;
    private const float SsaoFadeoutFrom = 50f;
    private const float SsaoFadeoutTo = 300f;

    private static void ApplyQualityLevels()
    {
        int atlas = ShadowQuality switch
        {
            QualityLevel.Low => ShadowAtlasLow,
            QualityLevel.High => ShadowAtlasHigh,
            _ => ShadowAtlasMedium,
        };
        var filter = ShadowQuality switch
        {
            QualityLevel.Low => RenderingServer.ShadowQuality.Hard,
            QualityLevel.High => RenderingServer.ShadowQuality.SoftMedium,
            _ => RenderingServer.ShadowQuality.SoftLow,
        };
        RenderingServer.DirectionalShadowAtlasSetSize(atlas, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(filter);
        RenderingServer.PositionalSoftShadowFilterSetQuality(filter);
        var ssao = SsaoQuality switch
        {
            QualityLevel.Low => RenderingServer.EnvironmentSsaoQuality.VeryLow,
            QualityLevel.High => RenderingServer.EnvironmentSsaoQuality.Medium,
            _ => RenderingServer.EnvironmentSsaoQuality.Low,
        };
        RenderingServer.EnvironmentSetSsaoQuality(ssao, true, SsaoAdaptiveTarget, SsaoBlurPasses, SsaoFadeoutFrom, SsaoFadeoutTo);
    }

    public static bool UpscaleSupported =>
        RenderingServer.GetCurrentRenderingMethod() == "forward_plus";

    private static UpscaleMode EffectiveUpscale =>
        UpscaleSupported || Upscale == UpscaleMode.Off ? Upscale : UpscaleMode.Off;

    public static void ApplyUiScale()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        if (Engine.GetMainLoop() is not SceneTree { Root: not null } tree) return;
        tree.Root.ContentScaleFactor = EffectiveUiScale * Platform.MenuScale;
        UiScaleApplied?.Invoke();
        if (_uiScaleFollowsWindow) return;
        _uiScaleFollowsWindow = true;
        tree.Root.SizeChanged += () =>
        {
            if (!UiScaleChosen) ApplyUiScale();
        };
    }

    public static void SetLighting(int brightness, int contrast, int nightBrightness, int fog)
    {
        Brightness = LightSliders.Brightness.Clamp(brightness);
        Contrast = LightSliders.Contrast.Clamp(contrast);
        NightBrightness = LightSliders.NightBrightness.Clamp(nightBrightness);
        Fog = LightSliders.Fog.Clamp(fog);
        Save();
        GraphicsChanged?.Invoke();
    }

    public static void SetViewDistance(float scale)
    {
        ViewDistance = Mathf.Clamp(scale, ViewDistanceMin, ViewDistanceMax);
        Save();
        GraphicsChanged?.Invoke();
    }

    public static void SetStickSensitivity(float move, float look)
    {
        MoveStickSensitivity = Mathf.Clamp(move, StickSensitivityMin, StickSensitivityMax);
        LookStickSensitivity = Mathf.Clamp(look, StickSensitivityMin, StickSensitivityMax);
        Save();
    }

    internal static void PreviewUiScale(float scale)
    {
        UiScale = Mathf.Clamp(scale, UiScaleMin, UiScaleMax);
        ApplyUiScale();
    }

    public static void SetLanguage(LibreKO.Network.GameLanguage language)
    {
        if (Language == language)
            return;
        Language = language;
        Save();
    }

    public static void SetUiScale(float scale)
    {
        float clamped = Mathf.Clamp(scale, UiScaleMin, UiScaleMax);
        if (!UiScaleChosen && Mathf.IsEqualApprox(clamped, EffectiveUiScale)) return;
        UiScale = clamped;
        UiScaleChosen = true;
        ApplyUiScale();
        Save();
    }

    public const float UiScaleSmallScreen = 0.8f;
    public const int SmallScreenHeight = 860;
    public static bool UiScaleChosen { get; private set; }
    public static event System.Action? UiScaleApplied;
    private static bool _uiScaleFollowsWindow;

    public static float EffectiveUiScale =>
        UiScaleChosen || Platform.TouchUi ? UiScale : AutoUiScale(WindowHeight());

    public static float AutoUiScale(int windowHeight) =>
        windowHeight > 0 && windowHeight < SmallScreenHeight ? UiScaleSmallScreen : UiScale;

    private static int WindowHeight() =>
        DisplayServer.GetName() == "headless" ? 0 : DisplayServer.WindowGetSize().Y;

    public static void SetNamePlateScale(float scale)
    {
        var clamped = Mathf.Clamp(scale, NamePlateScaleMin, NamePlateScaleMax);
        if (Mathf.IsEqualApprox(clamped, NamePlateScale))
            return;
        NamePlateScale = clamped;
        NamePlate.Rescale();
        Save();
    }

    public static void ApplyVideo()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        if (Screen >= 0 && Screen < DisplayServer.GetScreenCount() && DisplayServer.WindowGetCurrentScreen() != Screen)
            DisplayServer.WindowSetCurrentScreen(Screen);
        switch (WindowMode)
        {
            case VideoMode.Fullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
            case VideoMode.BorderlessFullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            default:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetSize(new Vector2I(WinWidth, WinHeight));
                int current = DisplayServer.WindowGetCurrentScreen();
                var screen = DisplayServer.ScreenGetSize(current);
                DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition(current)
                    + (screen - new Vector2I(WinWidth, WinHeight)) / 2);
                break;
        }
        DisplayServer.WindowSetVsyncMode(VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
    }

    private const string SettingsFileName = "settings.cfg";
    private const string WindowsFileName = "windows.cfg";
    private const string KeysFileName = "keys.cfg";
    private const string PresetsFileName = "presets.cfg";
    private const string LootFilterFileName = "lootfilter.cfg";
    private const string DefaultsResPath = "res://settings.default.cfg";

    public static string SettingsSavePath { get; private set; } = "";

    public static bool InstallDirWritable { get; private set; } = true;

    public static string InstallDir() => OS.HasFeature("editor")
        ? ProjectSettings.GlobalizePath("res://")
        : Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";

    private static string UserDir() => ProjectSettings.GlobalizePath("user://");

    private static bool DirIsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".gko_write_probe");
            File.WriteAllBytes(probe, System.Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static ConfigFile? _windowsCfg;

    private static ConfigFile WindowsCfg()
    {
        if (_windowsCfg != null) return _windowsCfg;
        _windowsCfg = new ConfigFile();
        _windowsCfg.Load(WindowsPath());
        return _windowsCfg;
    }

    public static Vector2 GetWindowPos(string id, Vector2 fallback)
    {
        var cfg = WindowsCfg();
        return cfg.HasSectionKey("pos", id) ? cfg.GetValue("pos", id, fallback).AsVector2() : fallback;
    }

    public static bool HasWindowPos(string id) => WindowsCfg().HasSectionKey("pos", id);

    public static void SaveWindowPos(string id, Vector2 pos)
    {
        var cfg = WindowsCfg();
        cfg.SetValue("pos", id, pos);
        ReportWindowsSave(cfg.Save(WindowsPath()));
    }

    public static void ForgetWindowPos(string id)
    {
        var cfg = WindowsCfg();
        if (!cfg.HasSectionKey("pos", id)) return;
        cfg.EraseSectionKey("pos", id);
        ReportWindowsSave(cfg.Save(WindowsPath()));
    }

    public static void ForgetAllWindowLayouts()
    {
        var cfg = WindowsCfg();
        foreach (string section in WindowLayoutSections)
            if (cfg.HasSection(section)) cfg.EraseSection(section);
        ReportWindowsSave(cfg.Save(WindowsPath()));
    }

    private static readonly string[] WindowLayoutSections = ["pos", "size"];

    public static void ForgetWindowPositionsOnce(string migration, System.Collections.Generic.IEnumerable<string> ids)
    {
        var cfg = WindowsCfg();
        if (cfg.HasSectionKey("migrations", migration)) return;
        foreach (string id in ids)
            if (cfg.HasSectionKey("pos", id)) cfg.EraseSectionKey("pos", id);
        cfg.SetValue("migrations", migration, true);
        ReportWindowsSave(cfg.Save(WindowsPath()));
    }

    public static Vector2 GetWindowSize(string id, Vector2 fallback)
    {
        var cfg = WindowsCfg();
        return cfg.HasSectionKey("size", id) ? cfg.GetValue("size", id, fallback).AsVector2() : fallback;
    }

    public static void SaveWindowSize(string id, Vector2 size)
    {
        var cfg = WindowsCfg();
        cfg.SetValue("size", id, size);
        ReportWindowsSave(cfg.Save(WindowsPath()));
    }

    private static bool _windowsSaveFailed;

    private static void ReportWindowsSave(Error err)
    {
        if (err == Error.Ok || _windowsSaveFailed) return;
        _windowsSaveFailed = true;
        GD.PushError($"[config] could not write {WindowsPath()}: {err} — window layout will not persist");
    }

    private static string WindowsPath() => Path.Combine(
        InstallDirWritable ? InstallDir() : UserDir(), WindowsFileName);

    private static ConfigFile? _keysCfg;

    private static ConfigFile KeysCfg()
    {
        if (_keysCfg != null) return _keysCfg;
        _keysCfg = new ConfigFile();
        _keysCfg.Load(KeysPath());
        return _keysCfg;
    }

    public static string GetKeyBind(string id)
    {
        var cfg = KeysCfg();
        return cfg.HasSectionKey("keys", id) ? cfg.GetValue("keys", id, "").AsString() : "";
    }

    public static void SaveKeyBind(string id, string chord)
    {
        var cfg = KeysCfg();
        cfg.SetValue("keys", id, chord);
        ReportKeysSave(cfg.Save(KeysPath()));
    }

    public static void ClearKeyBinds()
    {
        var cfg = KeysCfg();
        if (cfg.HasSection("keys")) cfg.EraseSection("keys");
        ReportKeysSave(cfg.Save(KeysPath()));
    }

    public static string GetPadBind(string id)
    {
        var cfg = KeysCfg();
        return cfg.HasSectionKey("pad", id) ? cfg.GetValue("pad", id, "").AsString() : "";
    }

    public static void SavePadBind(string id, string chord)
    {
        var cfg = KeysCfg();
        cfg.SetValue("pad", id, chord);
        ReportKeysSave(cfg.Save(KeysPath()));
    }

    public static void ClearPadBinds()
    {
        var cfg = KeysCfg();
        if (cfg.HasSection("pad")) cfg.EraseSection("pad");
        ReportKeysSave(cfg.Save(KeysPath()));
    }

    private static bool _keysSaveFailed;

    private static void ReportKeysSave(Error err)
    {
        if (err == Error.Ok || _keysSaveFailed) return;
        _keysSaveFailed = true;
        GD.PushError($"[config] could not write {KeysPath()}: {err} — key bindings will not persist");
    }

    private static string KeysPath() => Path.Combine(
        InstallDirWritable ? InstallDir() : UserDir(), KeysFileName);

    private static ConfigFile? _presetsCfg;

    private static ConfigFile PresetsCfg()
    {
        if (_presetsCfg != null) return _presetsCfg;
        _presetsCfg = new ConfigFile();
        _presetsCfg.Load(PresetsPath());
        return _presetsCfg;
    }

    public static int[] GetPresetPlan(string character, string kind, int slot, int length)
    {
        var cfg = PresetsCfg();
        string key = $"{kind}{slot}";
        var values = new int[length];
        if (!cfg.HasSectionKey(character, key)) return values;

        var stored = cfg.GetValue(character, key, new int[0]).AsInt32Array();
        for (int i = 0; i < length && i < stored.Length; i++) values[i] = stored[i];
        return values;
    }

    public static void SavePresetPlan(string character, string kind, int slot, int[] values)
    {
        var cfg = PresetsCfg();
        cfg.SetValue(character, $"{kind}{slot}", values);
        ReportPresetsSave(cfg.Save(PresetsPath()));
    }

    private static bool _presetsSaveFailed;

    private static void ReportPresetsSave(Error err)
    {
        if (err == Error.Ok || _presetsSaveFailed) return;
        _presetsSaveFailed = true;
        GD.PushError($"[config] could not write {PresetsPath()}: {err} — stat presets will not persist");
    }

    private static ConfigFile? _lootFilterCfg;

    private static ConfigFile LootFilterCfg()
    {
        if (_lootFilterCfg != null) return _lootFilterCfg;
        _lootFilterCfg = new ConfigFile();
        _lootFilterCfg.Load(LootFilterPath());
        return _lootFilterCfg;
    }

    public static LibreKO.Domain.LootFilter LoadLootFilter(string character)
    {
        var cfg = LootFilterCfg();
        var filter = new LibreKO.Domain.LootFilter();
        if (!cfg.HasSection(character)) return filter;
        filter.Enabled = cfg.GetValue(character, "enabled", false).AsBool();
        filter.MinSellPrice = cfg.GetValue(character, "min_price", 0).AsInt32();
        Fill(filter.Groups, cfg.GetValue(character, "groups", new int[0]).AsInt32Array(), g => (LibreKO.Domain.LootGroup)g);
        Fill(filter.Grades, cfg.GetValue(character, "grades", new int[0]).AsInt32Array(), g => g);
        Fill(filter.Always, cfg.GetValue(character, "always", new int[0]).AsInt32Array(), g => g);
        Fill(filter.Never, cfg.GetValue(character, "never", new int[0]).AsInt32Array(), g => g);
        return filter;
    }

    private static void Fill<T>(HashSet<T> target, int[] stored, Func<int, T> map)
    {
        target.Clear();
        foreach (int value in stored) target.Add(map(value));
    }

    public static void SaveLootFilter(string character, LibreKO.Domain.LootFilter filter)
    {
        var cfg = LootFilterCfg();
        cfg.SetValue(character, "enabled", filter.Enabled);
        cfg.SetValue(character, "min_price", filter.MinSellPrice);
        cfg.SetValue(character, "groups", filter.Groups.Select(g => (int)g).ToArray());
        cfg.SetValue(character, "grades", filter.Grades.ToArray());
        cfg.SetValue(character, "always", filter.Always.ToArray());
        cfg.SetValue(character, "never", filter.Never.ToArray());
        if (cfg.Save(LootFilterPath()) is var err and not Error.Ok)
            GD.PushError($"[config] could not write {LootFilterPath()}: {err}");
    }

    private static string LootFilterPath() => Path.Combine(
        InstallDirWritable ? InstallDir() : UserDir(), LootFilterFileName);

    private static string PresetsPath() => Path.Combine(
        InstallDirWritable ? InstallDir() : UserDir(), PresetsFileName);

    public static Color TooltipColor(int idx) =>
        _tooltipColors[Mathf.Clamp(idx, 0, _tooltipColors.Length - 1)];

    private const int RotatedTooltipColorFirst = 6;
    private static readonly uint[] RotatedTooltipColors = { 0xFFEAF50C, 0xFFADFF2F, 0xFFCDC300 };

    private static void RestoreRotatedTooltipColors()
    {
        for (int i = 0; i < RotatedTooltipColors.Length; i++)
            if ((uint)_tooltipColors[RotatedTooltipColorFirst + i].ToArgb32() != RotatedTooltipColors[i]) return;
        for (int i = 0; i < RotatedTooltipColors.Length; i++)
            _tooltipColors[RotatedTooltipColorFirst + i] = DefaultTooltipColors[RotatedTooltipColorFirst + i];
    }

    private static Color ReadTooltipColor(ConfigFile cfg, int idx)
    {
        string fallback = ArgbString(_tooltipColors[idx]);
        string s = cfg.GetValue("fontstate", $"tooltipcolor{idx}", fallback).AsString();
        return ParseArgb(s, _tooltipColors[idx]);
    }

    private static Color ParseArgb(string value, Color fallback)
    {
        string s = value.Trim();
        bool prefixedHex = s.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase) || s.StartsWith("#");
        if (s.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) s = s[2..];
        if (s.StartsWith("#")) s = s[1..];
        bool hasHexAlpha = false;
        foreach (char ch in s)
            if ((ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')) { hasHexAlpha = true; break; }
        if (!prefixedHex && !hasHexAlpha)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int signed))
                return Argb(unchecked((uint)signed));
            if (uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint unsigned))
                return Argb(unsigned);
        }
        if (uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
            return Argb(hex);
        return fallback;
    }

    private static string ArgbString(Color c)
    {
        uint a = (uint)Mathf.Clamp(Mathf.RoundToInt(c.A * 255f), 0, 255);
        uint r = (uint)Mathf.Clamp(Mathf.RoundToInt(c.R * 255f), 0, 255);
        uint g = (uint)Mathf.Clamp(Mathf.RoundToInt(c.G * 255f), 0, 255);
        uint b = (uint)Mathf.Clamp(Mathf.RoundToInt(c.B * 255f), 0, 255);
        return $"{a:X2}{r:X2}{g:X2}{b:X2}";
    }

    private static Color Argb(uint argb) => new(
        ((argb >> 16) & 0xFF) / 255f,
        ((argb >> 8) & 0xFF) / 255f,
        (argb & 0xFF) / 255f,
        ((argb >> 24) & 0xFF) / 255f);

    private static void ApplyCommandLineOverrides()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--host="))
                ServerHost = arg["--host=".Length..];
            else if (arg.StartsWith("--login-port=") && int.TryParse(arg["--login-port=".Length..], out var port))
                ServerPort = port;
            else if (arg.StartsWith("--game-port=") && int.TryParse(arg["--game-port=".Length..], out var gport))
                GamePort = gport;
            else if (arg.StartsWith("--version=") && int.TryParse(arg["--version=".Length..], out var ver))
                ServerVersion = ver;
            else if (arg.StartsWith("--ping="))
                PingEnabled = arg["--ping=".Length..] is "1" or "true" or "True";
            else if (arg.StartsWith("--dev="))
                Development = arg["--dev=".Length..] is "1" or "true" or "True";
        }
    }
}
