using Godot;

namespace LibreKO;

public enum Weather { Sunny, Rainy, Snow }

public partial class Sky : Node3D
{
    private WorldEnvironment worldEnvironment = null!;
    private Godot.Environment env = null!;
    private ShaderMaterial skyMat = null!;
    private DirectionalLight3D sun = null!;
    private DirectionalLight3D moon = null!;
    private GpuParticles3D rainParticles = null!;
    private GpuParticles3D snowParticles = null!;
    private StandardMaterial3D rainMaterial = null!;
    private StandardMaterial3D snowMaterial = null!;

    public const string WetnessParam = "weather_wetness";
    public const string RainParam = "weather_rain";
    public const string SnowParam = "weather_snow";
    public const string SkyParam = "weather_sky";
    private const float SkyStep = 0.004f;
    private const float WetPerSecond = 0.05f;
    private const float DryPerSecond = 0.012f;
    private const float SnowCoverPerSecond = 0.02f;
    private const float SnowMeltPerSecond = 0.01f;
    private const float RainLevelPerSecond = 0.5f;
    private const float LightRainWetness = 0.55f;
    private const float LightSnowCover = 0.45f;
    private const float GlobalStep = 0.002f;
    private const float BoltSpreadRadians = 0.6f;
    private const float BoltEchoDelay = 0.12f;
    private const float BoltEchoChance = 0.6f;
    private const float BoltEchoShare = 0.7f;
    private const float FlashDecayPerSecond = 4f;
    private const float BoltDecayPerSecond = 7f;
    private const float BoltSeedRange = 100f;
    private const float PrecipNightLight = 0.38f;
    private const float StormSkyEnergy = 0.82f;
    private const float StormAmbientLift = 0.75f;
    private const float OvercastAmbientLift = 0.5f;
    private const float OvercastNightLoss = 0.2f;
    private const float OvercastFillShare = 0.8f;
    private const float OvercastNightFill = 0.55f;
    private const float OvercastExposureLift = 0.12f;
    private const float OvercastSaturationLoss = 0.12f;
    private static readonly Color DayFill = new(0.64f, 0.70f, 0.80f);
    private static readonly Color OvercastFill = new(0.66f, 0.68f, 0.72f);
    private const float StormPrecipLight = 0.8f;
    private const float PrecipHeight = 16f;
    private const float RainFallSpeed = 75f;
    private const float SnowFallSpeed = 2.4f;
    private const float SnowDrift = 0.9f;
    private static readonly Color RainTint = new(0.70f, 0.76f, 0.86f, 0.30f);
    private static readonly Color SnowTint = new(1f, 1f, 1f, 0.92f);

    private float elapsed;
    private float wetTarget;
    private float rainTarget;
    private float snowTarget;
    private bool storm;
    private Weather lastWeather = Weather.Sunny;
    private float flash;
    private float bolt;
    private float echoIn = -1f;
    private float nextBolt = 5f;
    private float shownWetness = -1f;
    private float shownRain = -1f;
    private float shownSnow = -1f;
    private Vector3 shownSky = -Vector3.One;
    public float Wetness { get; private set; }
    public float RainLevel { get; private set; }
    public float SnowCover { get; private set; }
    public float RainOnScreen { get; private set; }
    public float SunShine { get; private set; }

    public float WeatherLight { get; set; } = 1f;

    public float WeatherShare { get; set; } = 1f;

    public float NightAmbient = 0.85f;
    public float NightSkyEnergy = 0.32f;
    public float NightMoonEnergy = 0.6f;
    public float MoonCloudBlock = 0.35f;
    public float NightAltitude = -22f;
    public float VolFogDensity = 0.003f;

    private const float DaylightAltitudeDeg = 3f;
    private const float StarAltitudeDeg = -5f;
    private const float GoldPeakAltitudeDeg = 0f;
    private const float GoldRiseSpanDeg = 14f;
    private const float GoldFallSpanDeg = 17f;
    private const float GoldStrength = 0.62f;

    private const float NightExposure = 1.3f;
    private const float NightSkyShare = 0.15f;
    private const float DaySaturation = 1.18f;
    private const float NightSaturation = 0.94f;
    private const float NightContrast = 1.03f;
    private static readonly Color NightFill = new(0.36f, 0.44f, 0.62f);

    private static readonly Color NightTop = new(0.025f, 0.04f, 0.09f);
    private static readonly Color DayTop = new(0.24f, 0.42f, 0.72f);
    private static readonly Color NightHorizon = new(0.06f, 0.08f, 0.15f);
    private static readonly Color DayHorizon = new(0.78f, 0.80f, 0.82f);
    private static readonly Color GoldHorizon = new(0.95f, 0.45f, 0.18f);
    private static readonly Color Overcast = new(0.46f, 0.48f, 0.52f);
    private static readonly Color StormTop = new(0.10f, 0.11f, 0.14f);
    private static readonly Color StormHorizon = new(0.20f, 0.21f, 0.24f);
    private static readonly Color SunDayColor = new(1.0f, 0.96f, 0.86f);
    private static readonly Color SunGoldColor = new(1.0f, 0.55f, 0.30f);
    private static readonly Color MoonLightHigh = new(0.68f, 0.77f, 1.00f);
    private static readonly Color MoonLightLow = new(0.95f, 0.78f, 0.60f);
    private static readonly Color MoonDiscHigh = new(0.94f, 0.95f, 1.00f);
    private static readonly Color MoonDiscLow = new(1.00f, 0.84f, 0.66f);

    private const float SunPeakAltitudeDeg = 60f;
    private const float SunAngularClear = 0f;
    private const float SunAngularOvercast = 1.2f;
    private const float SunShadowBlur = 0.6f;
    private const float SunShadowBias = 0.03f;
    private const float SunShadowNormalBias = 1.5f;
    private const float SunShadowSplit1 = 0.12f;
    private const float SunShadowSplit2 = 0.3f;
    private const float SunShadowSplit3 = 0.6f;
    private const float MoonAngularRadius = 0.075f;
    private const float MoonDiscBrightness = 1.75f;
    private const float MoonHaloStrength = 0.30f;
    private const float MoonPeakElevationDeg = 22f;
    private const float MoonLightLowDeg = 38f;
    private const float MoonLightHighDeg = 58f;
    private const float MoonRiseCurve = 0.45f;
    private const float MoonRiseBandDeg = 8f;
    private const float MoonOvercastHaze = 0.6f;

    private const string MoonTexPath = "res://assets/sky/moon.png";

    private static class Param
    {
        public static readonly StringName
            MoonDir = "moon_dir", MoonDisc = "moon_disc", MoonTint = "moon_tint",
            SkyTop = "sky_top", SkyHorizon = "sky_horizon", SkyEnergy = "sky_energy",
            SunTint = "sun_tint", SunDisc = "sun_disc", SunDir = "sun_dir", StarAmount = "star_amount",
            CloudTex = "cloud_tex", CloudPan = "cloud_pan", CirrusPan = "cirrus_pan",
            CloudScale = "cloud_scale", CloudCover = "cloud_cover", CloudHeight = "cloud_height",
            CloudDensity = "cloud_density", CloudLit = "cloud_lit", CloudDark = "cloud_dark",
            CirrusAmount = "cirrus_amount", Bolt = "bolt", BoltAzimuth = "bolt_azimuth", BoltSeed = "bolt_seed";
    }

    public override void _Ready()
    {
        skyMat = Shaders.Material("sky");
        skyMat.SetShaderParameter("sky_top", DayTop);
        skyMat.SetShaderParameter("sky_horizon", DayHorizon);
        skyMat.SetShaderParameter("ground_horizon", new Color(0.70f, 0.66f, 0.58f));
        skyMat.SetShaderParameter("ground_bottom", new Color(0.34f, 0.34f, 0.32f));
        skyMat.SetShaderParameter("moon_tex", ResourceLoader.Load<Texture2D>(MoonTexPath));
        skyMat.SetShaderParameter("moon_size", MoonAngularRadius);
        skyMat.SetShaderParameter("moon_bright", MoonDiscBrightness);
        skyMat.SetShaderParameter("moon_halo", MoonHaloStrength);
        env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Godot.Sky
            {
                SkyMaterial = skyMat,
                ProcessMode = Godot.Sky.ProcessModeEnum.Incremental,
                RadianceSize = Godot.Sky.RadianceSizeEnum.Size128,
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Bg,
            AmbientLightSkyContribution = 1.0f,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapWhite = 6.0f,
            SsaoRadius = 1.5f,
            SsaoIntensity = 2.0f,
            SsilEnabled = false,
            GlowIntensity = 0.7f,
            GlowStrength = 1.0f,
            GlowBloom = 0.05f,
            GlowHdrThreshold = 1.0f,
            GlowHdrLuminanceCap = 1.25f,
            VolumetricFogDensity = 0.003f,
            VolumetricFogAlbedo = new Color(0.92f, 0.90f, 0.86f),
            VolumetricFogAmbientInject = 0.0f,
            VolumetricFogLength = 512f,
            SsrEnabled = false,
            SsrMaxSteps = 24,
            SsrFadeIn = 0.2f,
            SsrFadeOut = 1.5f,
            SsrDepthTolerance = 0.3f,
            FogEnabled = true,
            FogDensity = BaseFogDensity,
            FogAerialPerspective = 0.18f,
            FogSkyAffect = 0.1f,
            AdjustmentEnabled = true,
            AdjustmentBrightness = BaseBrightness,
            AdjustmentContrast = BaseContrast,
            AdjustmentSaturation = DaySaturation,
        };
        worldEnvironment = new WorldEnvironment { Environment = env };
        AddChild(worldEnvironment);

        sun = new DirectionalLight3D
        {
            LightColor = SunDayColor,
            LightEnergy = 1.35f,
            LightAngularDistance = SunAngularClear,
            ShadowBias = SunShadowBias,
            ShadowNormalBias = SunShadowNormalBias,
            ShadowBlur = SunShadowBlur,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowSplit1 = SunShadowSplit1,
            DirectionalShadowSplit2 = SunShadowSplit2,
            DirectionalShadowSplit3 = SunShadowSplit3,
            DirectionalShadowMaxDistance = 100f,
            DirectionalShadowFadeStart = 0.8f,
        };
        AddChild(sun);

        moon = new DirectionalLight3D
        {
            LightColor = MoonLightHigh,
            LightEnergy = 0f,
            ShadowBias = 0.05f,
            DirectionalShadowMaxDistance = 500f,
        };
        AddChild(moon);

        rainParticles = BuildPrecip(rain: true, out rainMaterial);
        snowParticles = BuildPrecip(rain: false, out snowMaterial);
        AddChild(rainParticles);
        AddChild(snowParticles);

        ApplyGraphics();
    }

    private bool shadowsSkipped, ssaoSkipped;

    public override void _Process(double delta)
    {
        if (Perf.SkipShadows != shadowsSkipped)
        {
            shadowsSkipped = Perf.SkipShadows;
            sun.ShadowEnabled = Config.Shadows && !shadowsSkipped;
        }
        if (Perf.SkipSsao != ssaoSkipped && env != null)
        {
            ssaoSkipped = Perf.SkipSsao;
            ApplyAmbientOcclusion();
        }
        float dt = (float)delta;
        elapsed += dt;
        Wetness = Mathf.MoveToward(Wetness, wetTarget, dt * (wetTarget > Wetness ? WetPerSecond : DryPerSecond));
        SnowCover = Mathf.MoveToward(SnowCover, snowTarget, dt * (snowTarget > SnowCover ? SnowCoverPerSecond : SnowMeltPerSecond));
        RainLevel = Mathf.MoveToward(RainLevel, rainTarget, dt * RainLevelPerSecond);
        PushWeatherGlobals();
        ProcessClouds(dt);
        ProcessLightning(dt);
    }

    private void ProcessLightning(float dt)
    {
        if (storm)
        {
            nextBolt -= dt;
            if (nextBolt <= 0f)
            {
                AimBolt();
                Strike(GD.Randf() < 0.35f ? 1f : 0.6f);
                echoIn = GD.Randf() < BoltEchoChance ? BoltEchoDelay : -1f;
                nextBolt = 5f + GD.Randf() * 9f;
            }
        }
        if (echoIn > 0f)
        {
            echoIn -= dt;
            if (echoIn <= 0f) Strike(BoltEchoShare);
        }
        flash = Mathf.MoveToward(flash, 0f, dt * FlashDecayPerSecond);
        bolt = Mathf.MoveToward(bolt, 0f, dt * BoltDecayPerSecond);
        skyMat.SetShaderParameter(Param.Bolt, bolt);
    }

    private void Strike(float strength)
    {
        flash = Mathf.Max(flash, strength);
        bolt = strength;
    }

    private void AimBolt()
    {
        Vector3 forward = GetViewport()?.GetCamera3D() is { } camera ? -camera.GlobalBasis.Z : Vector3.Forward;
        float azimuth = Mathf.Atan2(forward.X, forward.Z) + (GD.Randf() * 2f - 1f) * BoltSpreadRadians;
        skyMat.SetShaderParameter(Param.BoltAzimuth, azimuth);
        skyMat.SetShaderParameter(Param.BoltSeed, GD.Randf() * BoltSeedRange);
    }

    private void PushWeatherGlobals()
    {
        Push(WetnessParam, Wetness, ref shownWetness);
        Push(RainParam, RainLevel, ref shownRain);
        Push(SnowParam, SnowCover, ref shownSnow);
    }

    private static void Push(string name, float value, ref float shown)
    {
        if (shown >= 0f && Mathf.Abs(value - shown) < GlobalStep && (value > 0f || shown == 0f)) return;
        shown = value;
        RenderingServer.GlobalShaderParameterSet(name, value);
    }

    private void PushSky(Vector3 sky)
    {
        if (shownSky.DistanceSquaredTo(sky) < SkyStep * SkyStep) return;
        shownSky = sky;
        RenderingServer.GlobalShaderParameterSet(SkyParam, sky);
    }

    internal void StrikeNow()
    {
        AimBolt();
        Strike(1f);
    }

    internal void SettleWeather()
    {
        Wetness = wetTarget;
        RainLevel = rainTarget;
        SnowCover = snowTarget;
        PushWeatherGlobals();
    }

    private const string DepthPrepassSetting = "rendering/driver/depth_prepass/enable";

    private void ApplyAmbientOcclusion()
    {
        bool ssao = Config.Ssao && !Perf.SkipSsao;
        env.SsaoEnabled = ssao;
        ProjectSettings.SetSetting(DepthPrepassSetting, ssao);
    }

    private const float BaseBrightness = 1.0f;
    private const float BaseContrast = 1.12f;
    private const float BaseFogDensity = 0.0003f;
    private float fogScale = 1f;
    private float nightScale = 1f;
    private float contrastScale = 1f;

    public void ApplyGraphics()
    {
        contrastScale = LightRange.Multiplier(Config.Contrast);
        env.AdjustmentBrightness = BaseBrightness * LightRange.Multiplier(Config.Brightness);
        env.AdjustmentContrast = BaseContrast * contrastScale;
        fogScale = LightRange.Multiplier(Config.Fog);
        nightScale = LightRange.Multiplier(Config.NightBrightness);
        if (env == null) return;
        ApplyAmbientOcclusion();
        env.GlowEnabled = Config.Bloom;
        env.VolumetricFogEnabled = Config.VolumetricFog;
        CloudsEnabled = Config.Clouds;
        sun.ShadowEnabled = Config.Shadows;
        sun.DirectionalShadowMode = Config.ShadowQuality == QualityLevel.Low
            ? DirectionalLight3D.ShadowMode.Parallel2Splits
            : DirectionalLight3D.ShadowMode.Parallel4Splits;
        moon.ShadowEnabled = false;
    }

    private static Vector3 SkyDir(float azDeg, float altDeg)
    {
        float a = Mathf.DegToRad(azDeg), e = Mathf.DegToRad(altDeg);
        float ce = Mathf.Cos(e);
        return new Vector3(Mathf.Sin(a) * ce, Mathf.Sin(e), Mathf.Cos(a) * ce);
    }

    // Dusk keys off SUN ALTITUDE, never the clock -- ATMOSPHERE_REFERENCE.md "Dusk is one curve".
    private float AmbientFor(float sunAltDeg) =>
        Mathf.SmoothStep(NightAltitude, DaylightAltitudeDeg, sunAltDeg);

    private float StarsFor(float sunAltDeg) =>
        1f - Mathf.SmoothStep(NightAltitude, Mathf.Min(StarAltitudeDeg, NightAltitude + 1f), sunAltDeg);

    private static float GoldFor(float sunAltDeg)
    {
        float span = sunAltDeg >= GoldPeakAltitudeDeg ? GoldRiseSpanDeg : GoldFallSpanDeg;
        float w = Mathf.Clamp(1f - Mathf.Abs(sunAltDeg - GoldPeakAltitudeDeg) / span, 0f, 1f);
        return w * w * (3f - 2f * w) * GoldStrength;
    }

    public void Tick(float dayFrac, Weather weather, Vector3 followPos)
    {
        dayFrac -= Mathf.Floor(dayFrac);
        lastWeather = weather;
        this.followPos = followPos;

        float sunAlt = Mathf.Sin((dayFrac - 0.25f) * Mathf.Tau) * SunPeakAltitudeDeg;
        float az = dayFrac * 360f + 20f;
        float moonAlt = MoonPeakElevationDeg * Mathf.Pow(
            Mathf.Clamp(-sunAlt / SunPeakAltitudeDeg, 0f, 1f), MoonRiseCurve);
        float moonWarm = Mathf.Clamp(moonAlt / MoonPeakElevationDeg, 0f, 1f);
        float day = Mathf.Clamp((sunAlt + 6f) / 12f, 0f, 1f);
        float amb = AmbientFor(sunAlt);
        float deepNight = (1f - amb) * (1f - amb);
        float moonLightAlt = Mathf.Lerp(Mathf.Max(-sunAlt, 0f),
            Mathf.Lerp(MoonLightLowDeg, MoonLightHighDeg, moonWarm), deepNight);
        sun.RotationDegrees = new Vector3(-sunAlt, az, 0f);
        moon.RotationDegrees = new Vector3(-moonLightAlt, az + 180f, 0f);
        sunDir = sun.Transform.Basis.Z.Normalized();
        moonDir = SkyDir(az + 180f, moonAlt);

        float gold = GoldFor(sunAlt);
        storm = weather == Weather.Rainy;
        float overcast = weather switch { Weather.Rainy => 0.95f, Weather.Snow => 0.7f, _ => 0f };
        gold *= 1f - 0.9f * overcast;
        Color cloudTop = storm ? StormTop : Overcast * 0.6f;
        Color cloudHor = storm ? StormHorizon : Overcast;

        Color top = NightTop.Lerp(DayTop, amb);
        Color hor = NightHorizon.Lerp(DayHorizon, amb).Lerp(GoldHorizon, gold);
        if (overcast > 0f)
        {
            float tintDay = overcast * amb;
            float tint = storm ? Mathf.Max(tintDay, overcast * 0.55f) : tintDay;
            top = top.Lerp(cloudTop, tint);
            hor = hor.Lerp(cloudHor, tint);
        }
        float skyEnergy = Mathf.Lerp(NightSkyEnergy * nightScale, 1.0f, amb) * (storm ? StormSkyEnergy : 1f);
        float ambient = Mathf.Lerp(NightAmbient * nightScale, 1.0f, amb)
                        * (1f + (storm ? StormAmbientLift : OvercastAmbientLift) * overcast * amb)
                        * (1f - OvercastNightLoss * overcast * (1f - amb));
        if (flash > 0.001f) { skyEnergy += flash * 1.3f; ambient += flash * 1.6f; }
        Color fill = NightFill.Lerp(DayFill, amb)
            .Lerp(OvercastFill * Mathf.Lerp(OvercastNightFill, 1f, amb), OvercastFillShare * overcast);
        float skyShare = Mathf.Lerp(NightSkyShare, 1f, amb) * (1f - OvercastFillShare * overcast);

        var sunLight = SunGoldColor.Lerp(SunDayColor, day);
        float cloudSun = ApplyClouds(day, amb, overcast, sunLight, hor);

        sun.Visible = sunAlt > -3f;
        sun.LightColor = sunLight.Lerp(new Color(0.74f, 0.80f, 0.93f), sunBlock * 0.35f);
        sun.LightEnergy = Mathf.Lerp(0f, 1.5f, day) * (1f - (storm ? 0.9f : 0.75f) * overcast) * cloudSun * WeatherLight;
        // NOT _sunBlock: a penumbra that breathes with cloud cover is what made tree shadows crawl.
        sun.LightAngularDistance = Mathf.Lerp(SunAngularClear, SunAngularOvercast, overcast);
        SunShine = day * (1f - 0.85f * overcast) * cloudSun;

        float moonRise = Mathf.Clamp(moonAlt / MoonRiseBandDeg, 0f, 1f);
        moonLit = moonRise * (1f - day);
        moon.Visible = moonLit > 0.002f;
        moon.LightColor = MoonLightLow.Lerp(MoonLightHigh, moonWarm);
        moon.LightEnergy = moonLit * NightMoonEnergy * nightScale * (1f - (storm ? 0.6f : 0.45f) * overcast)
                          * (1f - MoonCloudBlock * moonBlock);
        skyMat.SetShaderParameter(Param.MoonDir, moonDir);
        skyMat.SetShaderParameter(Param.MoonDisc, moonLit * (1f - MoonOvercastHaze * overcast));
        skyMat.SetShaderParameter(Param.MoonTint, MoonDiscLow.Lerp(MoonDiscHigh, moonWarm));

        skyMat.SetShaderParameter(Param.SkyTop, top);
        skyMat.SetShaderParameter(Param.SkyHorizon, hor);
        PushSky(new Vector3(top.R + hor.R, top.G + hor.G, top.B + hor.B) * (0.5f * skyEnergy));
        skyMat.SetShaderParameter(Param.SkyEnergy, skyEnergy);
        skyMat.SetShaderParameter(Param.SunTint, sunLight);
        skyMat.SetShaderParameter(Param.SunDisc, day * (1f - 0.95f * overcast));
        skyMat.SetShaderParameter(Param.StarAmount,
            StarsFor(sunAlt) * (1f - 0.8f * overcast) * (1f - flash));

        env.AmbientLightEnergy = ambient;
        env.AmbientLightColor = fill;
        env.AmbientLightSkyContribution = skyShare;
        env.TonemapExposure = Mathf.Lerp(1f, NightExposure, deepNight) * (1f + OvercastExposureLift * overcast * amb);
        env.AdjustmentSaturation = Mathf.Lerp(NightSaturation, DaySaturation, amb) * (1f - OvercastSaturationLoss * overcast);
        env.AdjustmentContrast = Mathf.Lerp(NightContrast, BaseContrast, amb) * contrastScale;
        env.VolumetricFogDensity = (VolFogDensity + overcast * (storm ? 0.016f : 0.01f)) * fogScale;
        env.FogDensity = (BaseFogDensity + overcast * (storm ? 0.0032f : 0.0018f)) * fogScale;
        env.FogLightColor = hor;
        env.VolumetricFogAlbedo = new Color(0.16f, 0.18f, 0.24f).Lerp(new Color(0.92f, 0.90f, 0.86f), amb);

        bool rain = storm, snow = weather == Weather.Snow;
        StartOrStop(rainParticles, rain);
        StartOrStop(snowParticles, snow);
        float ratio = Mathf.Clamp(WeatherShare / FullPrecipShare, 0f, 1f);
        if (!Mathf.IsEqualApprox(rainParticles.AmountRatio, ratio)) rainParticles.AmountRatio = ratio;
        if (!Mathf.IsEqualApprox(snowParticles.AmountRatio, ratio)) snowParticles.AmountRatio = ratio;
        wetTarget = rain ? Mathf.Lerp(LightRainWetness, 1f, ratio) : 0f;
        rainTarget = rain ? ratio : 0f;
        snowTarget = snow ? Mathf.Lerp(LightSnowCover, 1f, ratio) : 0f;
        RainOnScreen = rain ? ratio : 0f;
        env.SsrEnabled = false;
        if (!rain && !snow) return;

        float precipLight = Mathf.Clamp(Mathf.Lerp(PrecipNightLight, 1f, amb) * (storm ? StormPrecipLight : 1f) + flash, PrecipNightLight, 1f);
        var above = followPos + Vector3.Up * PrecipHeight;
        var wd = WindDir;
        if (rain)
        {
            rainParticles.GlobalPosition = above;
            Tint(rainMaterial, RainTint, precipLight);
            float gust = 0.28f + 0.12f * Mathf.Sin(elapsed * 0.6f) + 0.07f * Mathf.Sin(elapsed * 1.7f + 1f);
            var fall = (Vector3.Down + new Vector3(wd.X, 0f, wd.Y) * gust).Normalized();
            rainParticles.Quaternion = new Quaternion(Vector3.Down, fall);
            if (rainParticles.ProcessMaterial is ParticleProcessMaterial rainProcess)
                rainProcess.Gravity = fall * RainFallSpeed;
        }
        if (snow)
        {
            snowParticles.GlobalPosition = above;
            Tint(snowMaterial, SnowTint, precipLight);
            if (snowParticles.ProcessMaterial is ParticleProcessMaterial snowProcess)
                snowProcess.Gravity = new Vector3(wd.X * SnowDrift, -SnowFallSpeed, wd.Y * SnowDrift);
        }
    }

    private static void StartOrStop(GpuParticles3D particles, bool falling)
    {
        if (particles.Emitting == falling) return;
        particles.Emitting = falling;
        if (falling) particles.Restart();
    }

    private static void Tint(StandardMaterial3D material, Color tint, float light)
    {
        var color = new Color(tint.R * light, tint.G * light, tint.B * light, tint.A);
        if (!material.AlbedoColor.IsEqualApprox(color)) material.AlbedoColor = color;
    }

    private const int RainCount = 2600;
    private const int SnowCount = 2200;
    private const double RainLifetime = 0.9;
    private const double SnowLifetime = 7.0;

    private static GpuParticles3D BuildPrecip(bool rain, out StandardMaterial3D material)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = rain ? new Vector3(22f, 0.5f, 22f) : new Vector3(20f, 1.5f, 20f),
            Direction = new Vector3(0, -1, 0),
            Spread = 0f,
            Gravity = new Vector3(0, rain ? -RainFallSpeed : -SnowFallSpeed, 0),
            InitialVelocityMin = rain ? 26f : 0.8f,
            InitialVelocityMax = rain ? 34f : 2.0f,
            ScaleMin = rain ? 0.8f : 0.35f,
            ScaleMax = rain ? 1.3f : 1.5f,
        };
        if (!rain)
        {
            process.TurbulenceEnabled = true;
            process.TurbulenceNoiseStrength = 1.6f;
            process.TurbulenceNoiseScale = 0.6f;
            process.ColorInitialRamp = new GradientTexture1D
            {
                Gradient = new Gradient { Colors = [new Color(1f, 1f, 1f, 0.45f), Colors.White] },
            };
        }

        material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = rain ? BaseMaterial3D.BillboardModeEnum.FixedY : BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoColor = rain ? RainTint : SnowTint,
            AlbedoTexture = rain ? RainStreakTexture() : SnowFlakeTexture(),
            VertexColorUseAsAlbedo = !rain,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        var mesh = new QuadMesh
        {
            Size = rain ? new Vector2(0.03f, 1.3f) : new Vector2(0.15f, 0.15f),
            Material = material,
        };

        return new GpuParticles3D
        {
            Amount = rain ? RainCount : SnowCount,
            Lifetime = rain ? RainLifetime : SnowLifetime,
            Preprocess = rain ? RainLifetime : SnowLifetime,
            Emitting = false,
            ProcessMaterial = process,
            DrawPass1 = mesh,
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-30, -30, -30), new Vector3(60, 60, 60)),
        };
    }

    private const float FullPrecipShare = WeatherRules.OtherMaxAmount / 100f;

    private static ImageTexture? rainStreak;
    private static ImageTexture RainStreakTexture()
    {
        if (rainStreak != null) return rainStreak;
        const int w = 8, h = 64;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
        {
            float along = Mathf.Pow(Mathf.Sin((float)y / (h - 1) * Mathf.Pi), 0.6f);
            for (int x = 0; x < w; x++)
            {
                float across = Mathf.Sin((float)x / (w - 1) * Mathf.Pi);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, along * across));
            }
        }
        rainStreak = ImageTexture.CreateFromImage(img);
        return rainStreak;
    }

    private static ImageTexture? snowFlake;
    private static ImageTexture SnowFlakeTexture()
    {
        if (snowFlake != null) return snowFlake;
        const int size = 16;
        const float half = (size - 1) * 0.5f;
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float fromCentre = new Vector2(x - half, y - half).Length() / half;
                float alpha = Mathf.Clamp(1f - fromCentre, 0f, 1f);
                img.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, alpha)));
            }
        snowFlake = ImageTexture.CreateFromImage(img);
        return snowFlake;
    }
}
