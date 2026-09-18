using System.IO;
using System.Globalization;
using Godot;

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

    public static string GameHostFor(string advertisedIp) => Development ? "127.0.0.1" : advertisedIp;

    public enum VideoMode { Windowed, BorderlessFullscreen, Fullscreen }

    public static LibreKO.Network.GameLanguage Language { get; private set; } = LibreKO.Network.GameLanguage.English;
    public static VideoMode WindowMode { get; private set; } = VideoMode.Windowed;
    public static bool VSync { get; private set; } = true;
    public static int WinWidth { get; private set; } = 1280;
    public static int WinHeight { get; private set; } = 720;

    public const float UiScaleMin = 0.75f;
    public const float UiScaleMax = 2.0f;
    public const float UiScaleStep = 0.05f;
    public const float UiScaleMobileDefault = 1.6f;

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
    public static bool Shadows { get; private set; } = true;
    public static bool Ssao { get; private set; } = true;
    public static bool VolumetricFog { get; private set; } = false;
    public static bool Bloom { get; private set; } = true;
    public static bool Clouds { get; private set; } = true;
    public static bool Capes { get; private set; } = false;

    public const float ViewDistanceMin = 0.35f;
    public const float ViewDistanceMax = 1f;
    public const float ViewDistanceMobileDefault = 0.4f;
    public const FpsCap FpsMobileDefault = FpsCap.Fps30;

    public static float ViewDistance { get; private set; } = 1f;

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

    public const float CamTurnSpeedMin = 90f;
    public const float CamTurnSpeedMax = 1080f;
    public const float CamEdgePanSpeedMin = 20f;
    public const float CamEdgePanSpeedMax = 300f;
    public const float CamEdgePanSpeedRetail = 114.6f;
    public static float CamTurnSpeed { get; private set; } = 360f;
    public static bool CamEdgePan { get; private set; } = true;
    public static float CamEdgePanSpeed { get; private set; } = CamEdgePanSpeedRetail;


    public static bool FxAmbient { get; private set; } = true;
    public static int FxDistance { get; private set; } = 180;
    public static bool DamageNumbers { get; private set; } = true;
    public static bool CombatLog { get; private set; } = false;

    public static bool AudioEnabled { get; private set; } = true;
    public static bool AudioMuted { get; private set; } = false;
    public static float MasterVolume { get; private set; } = 0.8f;
    public static float MusicVolume { get; private set; } = 0.5f;
    public static float SfxVolume { get; private set; } = 0.9f;
    public static float UiVolume { get; private set; } = 0.7f;
    public static float VoiceVolume { get; private set; } = 0.9f;

    public static int TooltipHeight { get; private set; } = 11;
    public static bool TooltipBold { get; private set; } = false;
    public static bool TooltipBack { get; private set; } = false;
    private static readonly Color[] DefaultTooltipColors =
    {
        Argb(0xFFFFFFFF), Argb(0xFFADFF2F), Argb(0xFFF15F5F), Argb(0xFF5CD1E5),
        Argb(0xFF86E57F), Argb(0xFF1E90FF), Argb(0xFFEAF50C), Argb(0xFFADFF2F),
        Argb(0xFFCDC300), Argb(0xFFFFFF00), Argb(0xFFFFFF00), Argb(0xFF7FFF00),
        Argb(0xFFFFFFFF), Argb(0xFFFF0000),
        Argb(0xFFFFFFFF), Argb(0xFFD68CFF), Argb(0xFFFFCC33), Argb(0xFFFF66CC), Argb(0xFFFF9933),
    };

    public const int RarityNameRegular = 14;
    public const int RarityNameUpgrade = 15;
    public const int RarityNameUnique = 16;
    public const int RarityNameReverse = 17;
    public const int RarityNameReverseUnique = 18;
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

        if (!haveInstall && InstallDirWritable)
            Save();

        ApplyCommandLineOverrides();
        ApplyGraphicsToViewport();
        ApplyUiScale();
        SavedAccount.Load();
    }

    private static bool ReadLayer(string path, bool server, bool shipped = false)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(path) != Error.Ok)
            return false;

        if (server)
        {
            ServerHost = cfg.GetValue("server", "host", ServerHost).AsString();
            ServerPort = cfg.GetValue("server", "login_port", ServerPort).AsInt32();
            GamePort = cfg.GetValue("server", "game_port", GamePort).AsInt32();
            ServerVersion = cfg.GetValue("server", "version", ServerVersion).AsInt32();
            PingEnabled = cfg.GetValue("server", "ping_enabled", PingEnabled).AsBool();
            Development = cfg.GetValue("server", "development", Development).AsBool();
        }
        PatchUrl = cfg.GetValue("patch", "url", PatchUrl).AsString();
        LegacyQuestFallback = cfg.GetValue("quests", "legacy_fallback", LegacyQuestFallback).AsBool();
        WindowMode = ReadEnum(cfg, "video", "mode", WindowMode);
        Language = ReadEnum(cfg, "game", "language", Language);
        WinWidth = cfg.GetValue("video", "width", WinWidth).AsInt32();
        WinHeight = cfg.GetValue("video", "height", WinHeight).AsInt32();
        UiScale = Mathf.Clamp((float)cfg.GetValue("video", "ui_scale",
                              Platform.TouchUi ? UiScaleMobileDefault : UiScale).AsDouble(),
                              UiScaleMin, UiScaleMax);
        NamePlateScale = Mathf.Clamp((float)cfg.GetValue("video", "name_plate_scale", NamePlateScale).AsDouble(),
                                     NamePlateScaleMin, NamePlateScaleMax);
        MoveStickSensitivity = Mathf.Clamp(
            (float)cfg.GetValue("controls", "move_stick", MoveStickSensitivity).AsDouble(),
            StickSensitivityMin, StickSensitivityMax);
        LookStickSensitivity = Mathf.Clamp(
            (float)cfg.GetValue("controls", "look_stick", LookStickSensitivity).AsDouble(),
            StickSensitivityMin, StickSensitivityMax);
        Shadows = cfg.GetValue("graphics", "shadows", Shadows).AsBool();
        Ssao = cfg.GetValue("graphics", "ssao", Ssao).AsBool();
        VolumetricFog = cfg.GetValue("graphics", "volumetric_fog", VolumetricFog).AsBool();
        Bloom = cfg.GetValue("graphics", "bloom", Bloom).AsBool();
        Clouds = cfg.GetValue("graphics", "clouds", Clouds).AsBool();
        Capes = cfg.GetValue("graphics", "capes", Capes).AsBool();
        AntiAlias = ReadEnum(cfg, "graphics", "aa", AntiAlias);
        Upscale = ReadEnum(cfg, "graphics", "upscale", Upscale);
        UpscaleQuality = ReadEnum(cfg, "graphics", "upscale_quality", UpscaleQuality);
        VSync = cfg.GetValue("video", "vsync", VSync).AsBool();
        if (shipped && Platform.TouchUi) FpsLimit = FpsMobileDefault;
        else FpsLimit = ReadEnum(cfg, "graphics", "fps_limit", FpsLimit);
        ShowStats = cfg.GetValue("hud", "show_stats", ShowStats).AsBool();
        CamTurnSpeed = Mathf.Clamp((float)cfg.GetValue("controls", "cam_turn_speed", CamTurnSpeed).AsDouble(),
            CamTurnSpeedMin, CamTurnSpeedMax);
        CamEdgePan = cfg.GetValue("controls", "cam_edge_pan", CamEdgePan).AsBool();
        CamEdgePanSpeed = Mathf.Clamp((float)cfg.GetValue("controls", "cam_edge_pan_speed", CamEdgePanSpeed).AsDouble(),
            CamEdgePanSpeedMin, CamEdgePanSpeedMax);
        FxAmbient = cfg.GetValue("effects", "ambient", FxAmbient).AsBool();
        FxDistance = Mathf.Clamp(cfg.GetValue("effects", "distance", FxDistance).AsInt32(), 40, 400);
        TouchUi = ReadEnum(cfg, "video", "touch_ui", TouchUi);
        ViewDistance = Mathf.Clamp((float)cfg.GetValue("graphics", "view_distance",
            Platform.TouchUi ? ViewDistanceMobileDefault : ViewDistance).AsDouble(),
            ViewDistanceMin, ViewDistanceMax);
        DamageNumbers = cfg.GetValue("effects", "damage_numbers", DamageNumbers).AsBool();
        CombatLog = cfg.GetValue("hud", "combat_log", CombatLog).AsBool();
        AudioEnabled = cfg.GetValue("audio", "enabled", AudioEnabled).AsBool();
        AudioMuted = cfg.GetValue("audio", "muted", AudioMuted).AsBool();
        MasterVolume = Vol(cfg, "master", MasterVolume);
        MusicVolume = Vol(cfg, "music", MusicVolume);
        SfxVolume = Vol(cfg, "sfx", SfxVolume);
        UiVolume = Vol(cfg, "ui", UiVolume);
        VoiceVolume = Vol(cfg, "voice", VoiceVolume);
        TooltipHeight = Mathf.Max(1, cfg.GetValue("fontstate", "tooltipheight", TooltipHeight).AsInt32());
        TooltipBold = cfg.GetValue("fontstate", "tooltipbold", TooltipBold).AsBool();
        TooltipBack = cfg.GetValue("fontstate", "tooltipback", TooltipBack).AsBool();
        for (int i = 0; i < _tooltipColors.Length; i++)
            _tooltipColors[i] = ReadTooltipColor(cfg, i);
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
        cfg.SetValue("video", "mode", (int)WindowMode);
        cfg.SetValue("video", "width", WinWidth);
        cfg.SetValue("video", "height", WinHeight);
        cfg.SetValue("video", "ui_scale", UiScale);
        cfg.SetValue("video", "name_plate_scale", NamePlateScale);
        cfg.SetValue("game", "language", (int)Language);
        cfg.SetValue("video", "touch_ui", (int)TouchUi);
        cfg.SetValue("graphics", "shadows", Shadows);
        cfg.SetValue("graphics", "ssao", Ssao);
        cfg.SetValue("graphics", "volumetric_fog", VolumetricFog);
        cfg.SetValue("graphics", "bloom", Bloom);
        cfg.SetValue("graphics", "clouds", Clouds);
        cfg.SetValue("graphics", "capes", Capes);
        cfg.SetValue("graphics", "aa", (int)AntiAlias);
        cfg.SetValue("graphics", "upscale", (int)Upscale);
        cfg.SetValue("graphics", "upscale_quality", (int)UpscaleQuality);
        cfg.SetValue("video", "vsync", VSync);
        cfg.SetValue("graphics", "fps_limit", (int)FpsLimit);
        cfg.SetValue("hud", "show_stats", ShowStats);
        cfg.SetValue("controls", "move_stick", MoveStickSensitivity);
        cfg.SetValue("controls", "look_stick", LookStickSensitivity);
        cfg.SetValue("controls", "cam_turn_speed", CamTurnSpeed);
        cfg.SetValue("controls", "cam_edge_pan", CamEdgePan);
        cfg.SetValue("controls", "cam_edge_pan_speed", CamEdgePanSpeed);
        cfg.SetValue("effects", "ambient", FxAmbient);
        cfg.SetValue("effects", "distance", FxDistance);
        cfg.SetValue("graphics", "view_distance", ViewDistance);
        cfg.SetValue("effects", "damage_numbers", DamageNumbers);
        cfg.SetValue("hud", "combat_log", CombatLog);
        cfg.SetValue("audio", "enabled", AudioEnabled);
        cfg.SetValue("audio", "muted", AudioMuted);
        cfg.SetValue("audio", "master", MasterVolume);
        cfg.SetValue("audio", "music", MusicVolume);
        cfg.SetValue("audio", "sfx", SfxVolume);
        cfg.SetValue("audio", "ui", UiVolume);
        cfg.SetValue("audio", "voice", VoiceVolume);
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
        Save();
    }

    public static void SetGraphics(bool shadows, bool ssao, bool volumetricFog, bool bloom, bool clouds,
                                   bool capes, AaMode aa, UpscaleMode upscale, UpscaleLevel quality,
                                   FpsCap fps)
    {
        Upscale = upscale;
        UpscaleQuality = quality;
        Shadows = shadows;
        Ssao = ssao;
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

    public static void SetEffects(bool ambient, int distance, bool damageNumbers, bool combatLog)
    {
        FxAmbient = ambient;
        FxDistance = Mathf.Clamp(distance, 40, 400);
        DamageNumbers = damageNumbers;
        CombatLog = combatLog;
        Save();
        EffectsChanged?.Invoke();
    }

    public static void SetShowStats(bool show)
    {
        if (ShowStats == show) return;
        ShowStats = show;
        Save();
    }

    public static void SetControls(float camTurnSpeed, bool camEdgePan, float camEdgePanSpeed)
    {
        CamTurnSpeed = Mathf.Clamp(camTurnSpeed, CamTurnSpeedMin, CamTurnSpeedMax);
        CamEdgePan = camEdgePan;
        CamEdgePanSpeed = Mathf.Clamp(camEdgePanSpeed, CamEdgePanSpeedMin, CamEdgePanSpeedMax);
        Save();
    }

    public static void ApplyGraphicsToViewport()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        Engine.MaxFps = FpsValue(FpsLimit);
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

    public static bool UpscaleSupported =>
        RenderingServer.GetCurrentRenderingMethod() == "forward_plus";

    private static UpscaleMode EffectiveUpscale =>
        UpscaleSupported || Upscale == UpscaleMode.Off ? Upscale : UpscaleMode.Off;

    public static void ApplyUiScale()
    {
        if (DisplayServer.GetName() == "headless")
            return;
        if (Engine.GetMainLoop() is SceneTree { Root: not null } tree)
            tree.Root.ContentScaleFactor = UiScale * Platform.MenuScale;
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
        UiScale = Mathf.Clamp(scale, UiScaleMin, UiScaleMax);
        ApplyUiScale();
        Save();
    }

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
                var screen = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
                DisplayServer.WindowSetPosition((screen - new Vector2I(WinWidth, WinHeight)) / 2);
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

    private static string PresetsPath() => Path.Combine(
        InstallDirWritable ? InstallDir() : UserDir(), PresetsFileName);

    public static Color TooltipColor(int idx) =>
        _tooltipColors[Mathf.Clamp(idx, 0, _tooltipColors.Length - 1)];

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
