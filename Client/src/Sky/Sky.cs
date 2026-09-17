using Godot;

namespace LibreKO;

public enum Weather { Sunny, Windy, Rainy, Snow }

public partial class Sky : Node3D
{
    private WorldEnvironment _we = null!;
    private Godot.Environment _env = null!;
    private ShaderMaterial _skyMat = null!;
    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _moon = null!;
    private GpuParticles3D _rain = null!;
    private GpuParticles3D _snow = null!;

    private float _t;
    private float _wetTarget;
    private bool _storm;
    private Weather _lastWeather = Weather.Sunny;
    private float _flash;
    private float _nextBolt = 5f;
    public float Wetness { get; private set; }
    public float SunShine { get; private set; }

    public float NightAmbient = 0.08f;
    public float NightSkyEnergy = 0.10f;
    public float NightMoonEnergy = 1.1f;
    public float MoonCloudBlock = 0.35f;
    public float NightAltitude = -22f;
    public float VolFogDensity = 0.003f;

    private const float DaylightAltitudeDeg = 3f;
    private const float StarAltitudeDeg = -5f;
    private const float GoldPeakAltitudeDeg = 0f;
    private const float GoldRiseSpanDeg = 14f;
    private const float GoldFallSpanDeg = 17f;
    private const float GoldStrength = 0.62f;

    private static readonly Color NightTop = new(0.015f, 0.025f, 0.06f);
    private static readonly Color DayTop = new(0.24f, 0.42f, 0.72f);
    private static readonly Color NightHorizon = new(0.04f, 0.05f, 0.10f);
    private static readonly Color DayHorizon = new(0.78f, 0.80f, 0.82f);
    private static readonly Color GoldHorizon = new(0.95f, 0.45f, 0.18f);
    private static readonly Color Overcast = new(0.46f, 0.48f, 0.52f);
    private static readonly Color StormTop = new(0.10f, 0.11f, 0.14f);
    private static readonly Color StormHorizon = new(0.20f, 0.21f, 0.24f);
    private static readonly Color SunDayColor = new(1.0f, 0.96f, 0.86f);
    private static readonly Color SunGoldColor = new(1.0f, 0.55f, 0.30f);
    private static readonly Color MoonLightHigh = new(0.55f, 0.66f, 0.95f);
    private static readonly Color MoonLightLow = new(1.00f, 0.72f, 0.46f);
    private static readonly Color MoonDiscHigh = new(0.94f, 0.95f, 1.00f);
    private static readonly Color MoonDiscLow = new(1.00f, 0.84f, 0.66f);

    private const float SunPeakAltitudeDeg = 60f;
    private const float SunAngularClear = 1.2f;
    private const float SunAngularOvercast = 2.4f;
    private const float MoonAngularRadius = 0.075f;
    private const float MoonDiscBrightness = 1.75f;
    private const float MoonHaloStrength = 0.30f;
    private const float MoonPeakElevationDeg = 22f;
    private const float MoonRiseCurve = 0.45f;
    private const float MoonRiseBandDeg = 8f;
    private const float MoonOvercastHaze = 0.6f;

    private const string MoonTexPath = "res://assets/sky/moon.png";

    public override void _Ready()
    {
        _skyMat = Shaders.Material("sky");
        _skyMat.SetShaderParameter("sky_top", DayTop);
        _skyMat.SetShaderParameter("sky_horizon", DayHorizon);
        _skyMat.SetShaderParameter("ground_horizon", new Color(0.70f, 0.66f, 0.58f));
        _skyMat.SetShaderParameter("ground_bottom", new Color(0.34f, 0.34f, 0.32f));
        _skyMat.SetShaderParameter("moon_tex", ResourceLoader.Load<Texture2D>(MoonTexPath));
        _skyMat.SetShaderParameter("moon_size", MoonAngularRadius);
        _skyMat.SetShaderParameter("moon_bright", MoonDiscBrightness);
        _skyMat.SetShaderParameter("moon_halo", MoonHaloStrength);
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Godot.Sky
            {
                SkyMaterial = _skyMat,
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
            FogDensity = 0.0003f,
            FogAerialPerspective = 0.18f,
            FogSkyAffect = 0.1f,
            AdjustmentEnabled = true,
            AdjustmentBrightness = 1.0f,
            AdjustmentContrast = 1.12f,
            AdjustmentSaturation = 1.18f,
        };
        _we = new WorldEnvironment { Environment = _env };
        AddChild(_we);

        _sun = new DirectionalLight3D
        {
            LightColor = SunDayColor,
            LightEnergy = 1.35f,
            LightAngularDistance = 1.0f,
            ShadowBias = 0.04f,
            ShadowNormalBias = 2.8f,
            ShadowBlur = 1.5f,
            DirectionalShadowBlendSplits = true,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
            DirectionalShadowMaxDistance = 100f,
            DirectionalShadowFadeStart = 0.8f,
        };
        AddChild(_sun);

        _moon = new DirectionalLight3D
        {
            LightColor = MoonLightHigh,
            LightEnergy = 0f,
            ShadowBias = 0.05f,
            DirectionalShadowMaxDistance = 500f,
        };
        AddChild(_moon);

        _rain = BuildPrecip(rain: true);
        _snow = BuildPrecip(rain: false);
        AddChild(_rain);
        AddChild(_snow);

        ApplyGraphics();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        Wetness = Mathf.MoveToward(Wetness, _wetTarget, dt * 0.4f);
        ProcessClouds(dt);

        if (_storm)
        {
            _nextBolt -= dt;
            if (_nextBolt <= 0f)
            {
                _flash = GD.Randf() < 0.35f ? 1f : 0.6f;
                _nextBolt = 5f + GD.Randf() * 9f;
            }
        }
        _flash = Mathf.MoveToward(_flash, 0f, dt * 4f);
    }

    public void ApplyGraphics()
    {
        if (_env == null) return;
        _env.SsaoEnabled = Config.Ssao;
        _env.GlowEnabled = Config.Bloom;
        _env.VolumetricFogEnabled = Config.VolumetricFog;
        CloudsEnabled = Config.Clouds;
        _sun.ShadowEnabled = Config.Shadows;
        _moon.ShadowEnabled = false;
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
        _lastWeather = weather;
        _followPos = followPos;

        float sunAlt = Mathf.Sin((dayFrac - 0.25f) * Mathf.Tau) * SunPeakAltitudeDeg;
        float az = dayFrac * 360f + 20f;
        float moonAlt = MoonPeakElevationDeg * Mathf.Pow(
            Mathf.Clamp(-sunAlt / SunPeakAltitudeDeg, 0f, 1f), MoonRiseCurve);
        _sun.RotationDegrees = new Vector3(-sunAlt, az, 0f);
        _moon.RotationDegrees = new Vector3(sunAlt, az + 180f, 0f);
        _sunDir = _sun.Transform.Basis.Z.Normalized();
        _moonDir = SkyDir(az + 180f, moonAlt);

        float day = Mathf.Clamp((sunAlt + 6f) / 12f, 0f, 1f);
        float amb = AmbientFor(sunAlt);
        float gold = GoldFor(sunAlt);
        _storm = weather == Weather.Rainy;
        float overcast = weather switch { Weather.Windy => 0.5f, Weather.Rainy => 0.95f, Weather.Snow => 0.7f, _ => 0f };
        gold *= 1f - 0.9f * overcast;
        Color cloudTop = _storm ? StormTop : Overcast * 0.6f;
        Color cloudHor = _storm ? StormHorizon : Overcast;

        Color top = NightTop.Lerp(DayTop, amb);
        Color hor = NightHorizon.Lerp(DayHorizon, amb).Lerp(GoldHorizon, gold);
        if (overcast > 0f)
        {
            float tintDay = overcast * amb;
            float tint = _storm ? Mathf.Max(tintDay, overcast * 0.55f) : tintDay;
            top = top.Lerp(cloudTop, tint);
            hor = hor.Lerp(cloudHor, tint);
        }
        float skyEnergy = Mathf.Lerp(NightSkyEnergy, 1.0f, amb) * (_storm ? 0.72f : 1f);
        float ambient = Mathf.Lerp(NightAmbient, 1.0f, amb) * (1f - (_storm ? 0.5f : 0.3f) * overcast);
        if (_flash > 0.001f) { skyEnergy += _flash * 1.3f; ambient += _flash * 1.6f; }

        var sunLight = SunGoldColor.Lerp(SunDayColor, day);
        float cloudSun = ApplyClouds(day, amb, overcast, sunLight, hor);

        _sun.Visible = sunAlt > -3f;
        _sun.LightColor = sunLight.Lerp(new Color(0.74f, 0.80f, 0.93f), _sunBlock * 0.35f);
        _sun.LightEnergy = Mathf.Lerp(0f, 1.5f, day) * (1f - (_storm ? 0.9f : 0.75f) * overcast) * cloudSun;
        // NOT _sunBlock: a penumbra that breathes with cloud cover is what made tree shadows crawl.
        _sun.LightAngularDistance = Mathf.Lerp(SunAngularClear, SunAngularOvercast, overcast);
        SunShine = day * (1f - 0.85f * overcast) * cloudSun;

        float moonRise = Mathf.Clamp(moonAlt / MoonRiseBandDeg, 0f, 1f);
        float moonWarm = Mathf.Clamp(moonAlt / MoonPeakElevationDeg, 0f, 1f);
        _moonLit = moonRise * (1f - day);
        _moon.Visible = _moonLit > 0.002f;
        _moon.LightColor = MoonLightLow.Lerp(MoonLightHigh, moonWarm);
        _moon.LightEnergy = _moonLit * NightMoonEnergy * (1f - (_storm ? 0.7f : 0.5f) * overcast)
                          * (1f - MoonCloudBlock * _moonBlock);
        _skyMat.SetShaderParameter("moon_dir", _moonDir);
        _skyMat.SetShaderParameter("moon_disc", _moonLit * (1f - MoonOvercastHaze * overcast));
        _skyMat.SetShaderParameter("moon_tint", MoonDiscLow.Lerp(MoonDiscHigh, moonWarm));

        _skyMat.SetShaderParameter("sky_top", top);
        _skyMat.SetShaderParameter("sky_horizon", hor);
        _skyMat.SetShaderParameter("sky_energy", skyEnergy);
        _skyMat.SetShaderParameter("sun_tint", sunLight);
        _skyMat.SetShaderParameter("sun_disc", day * (1f - 0.95f * overcast));
        _skyMat.SetShaderParameter("star_amount",
            StarsFor(sunAlt) * (1f - 0.8f * overcast) * (1f - _flash));

        _env.AmbientLightEnergy = ambient;
        _env.VolumetricFogDensity = VolFogDensity + overcast * (_storm ? 0.02f : 0.012f);
        _env.FogDensity = 0.0003f + overcast * (_storm ? 0.004f : 0.0025f);
        _env.FogLightColor = hor;
        _env.VolumetricFogAlbedo = new Color(0.16f, 0.18f, 0.24f).Lerp(new Color(0.92f, 0.90f, 0.86f), amb);

        bool rain = weather == Weather.Rainy, snow = weather == Weather.Snow;
        _rain.Emitting = rain;
        _snow.Emitting = snow;
        _wetTarget = rain ? 1f : 0f;
        _env.SsrEnabled = false;
        if (rain || snow)
        {
            var p = followPos; p.Y += 16f;
            _rain.GlobalPosition = p;
            _snow.GlobalPosition = p;
        }
        if (rain)
        {
            float gust = 0.28f + 0.12f * Mathf.Sin(_t * 0.6f) + 0.07f * Mathf.Sin(_t * 1.7f + 1f);
            var wd = WindDir;
            var wind = new Vector3(wd.X, 0f, wd.Y) * gust;
            var fall = (Vector3.Down + wind).Normalized();
            _rain.Quaternion = new Quaternion(Vector3.Down, fall);
            if (_rain.ProcessMaterial is ParticleProcessMaterial pm)
                pm.Gravity = fall * 75f;
        }
    }

    private static GpuParticles3D BuildPrecip(bool rain)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(26f, 0.5f, 26f),
            Direction = new Vector3(0, -1, 0),
            Spread = 0f,
            Gravity = new Vector3(0, rain ? -60f : -3f, 0),
            InitialVelocityMin = rain ? 22f : 1.5f,
            InitialVelocityMax = rain ? 30f : 3.0f,
            ScaleMin = rain ? 1.0f : 0.6f,
            ScaleMax = rain ? 1.4f : 1.2f,
        };
        if (!rain)
        {
            pm.TurbulenceEnabled = true;
            pm.TurbulenceNoiseStrength = 1.2f;
            pm.TurbulenceNoiseScale = 1.0f;
        }

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = rain ? BaseMaterial3D.BillboardModeEnum.FixedY : BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoColor = rain ? new Color(0.66f, 0.73f, 0.84f, 0.42f) : new Color(1f, 1f, 1f, 0.85f),
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        if (rain) mat.AlbedoTexture = RainStreakTexture();
        var mesh = new QuadMesh
        {
            Size = rain ? new Vector2(0.045f, 1.15f) : new Vector2(0.12f, 0.12f),
            Material = mat,
        };

        return new GpuParticles3D
        {
            Amount = rain ? 2200 : 700,
            Lifetime = rain ? 1.1 : 6.0,
            Emitting = false,
            ProcessMaterial = pm,
            DrawPass1 = mesh,
            LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-30, -30, -30), new Vector3(60, 60, 60)),
        };
    }

    private static ImageTexture? _rainStreak;
    private static ImageTexture RainStreakTexture()
    {
        if (_rainStreak != null) return _rainStreak;
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
        _rainStreak = ImageTexture.CreateFromImage(img);
        return _rainStreak;
    }
}
