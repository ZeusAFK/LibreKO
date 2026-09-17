using Godot;

namespace LibreKO;

public partial class Sky
{
    public float CloudBias;
    public float CloudScale = 0.00080f;
    public float CloudHeight = 700f;
    public float CloudDensity = 1.0f;
    public float CirrusAmount = 0.18f;
    public float WindSpeed = 1.0f;
    public float WindAngleDeg = 35f;
    public float SunCloudInfluence = 0.55f;
    public float CloudShadowStrength = 0.34f;
    public bool CloudsEnabled = true;

    public Vector3 MoonDir => _moonDir;
    public Vector3 SunDir => _sunDir;

    private Vector2 _cloudPan;
    private Vector2 _cirrusPan;
    private float _cloudCover = 0.45f;
    private float _coverTarget = 0.45f;
    private bool _coverInit;
    private float _sunBlock;
    private float _moonBlock;
    private float _moonLit;
    private Vector3 _sunDir = Vector3.Up;
    private Vector3 _moonDir = Vector3.Down;
    private Vector3 _followPos;
    private ShaderMaterial? _skyTexOn;

    // At 16 u/s a cloud crossed the sun every ~40s and every shadow pulsed with it.
    private static float BaseWindSpeed(Weather w) => w switch
    {
        Weather.Windy => 11f,
        Weather.Rainy => 14f,
        Weather.Snow => 5.5f,
        _ => 4f,
    };

    private static float CoverFor(Weather w) => w switch
    {
        Weather.Windy => 0.62f,
        Weather.Rainy => 0.95f,
        Weather.Snow => 0.78f,
        _ => 0.45f,
    };

    private Vector2 WindDir
    {
        get
        {
            float a = Mathf.DegToRad(WindAngleDeg);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
    }

    private void ProcessClouds(float dt)
    {
        var wind = WindDir;
        float speed = BaseWindSpeed(_lastWeather) * WindSpeed;
        _cloudPan += wind * speed * dt;
        _cirrusPan += wind * speed * 2.4f * dt;

        _cloudPan = _cloudPan.PosMod(1f / Mathf.Max(CloudScale, 1e-6f));
        _cirrusPan = _cirrusPan.PosMod(1f / Mathf.Max(CloudScale * 0.4f, 1e-6f));

        _coverTarget = Mathf.Clamp(CoverFor(_lastWeather) + CloudBias, 0f, 1f);
        if (_coverInit) _cloudCover = Mathf.MoveToward(_cloudCover, _coverTarget, dt * 0.12f);
        else { _cloudCover = _coverTarget; _coverInit = true; }

        float k = 1f - Mathf.Exp(-dt * 1.2f);
        float sunTarget = CloudsEnabled && _sunDir.Y > 0f
            ? CloudField.CoverAt(CloudField.DeckHit(_followPos, _sunDir, CloudHeight), _cloudPan, CloudScale, _cloudCover)
            : 0f;
        _sunBlock = Mathf.Lerp(_sunBlock, sunTarget, k);

        float moonTarget = CloudsEnabled && _moonLit > 0.002f
            ? CloudField.CoverAt(CloudField.DeckHit(_followPos, _moonDir, CloudHeight), _cloudPan, CloudScale, _cloudCover)
            : 0f;
        _moonBlock = Mathf.Lerp(_moonBlock, moonTarget, k);
    }

    private float ApplyClouds(float day, float amb, float overcast, Color sunLight, Color horizon)
    {
        var litDay = sunLight.Lerp(Colors.White, 0.35f) * Mathf.Lerp(0.62f, 1.0f, day);
        var darkDay = horizon.Lerp(new Color(0.44f, 0.48f, 0.56f), 0.55f) * Mathf.Lerp(0.55f, 0.88f, day);
        var lit = new Color(0.07f, 0.09f, 0.14f).Lerp(litDay, amb);
        var dark = new Color(0.02f, 0.03f, 0.05f).Lerp(darkDay, amb);
        if (_storm)
        {
            lit = lit.Lerp(new Color(0.26f, 0.27f, 0.31f), 0.85f);
            dark = dark.Lerp(new Color(0.08f, 0.09f, 0.12f), 0.85f);
        }

        if (!ReferenceEquals(_skyMat, _skyTexOn)) { _skyMat.SetShaderParameter("cloud_tex", CloudField.Texture); _skyTexOn = _skyMat; }
        _skyMat.SetShaderParameter("cloud_pan", _cloudPan);
        _skyMat.SetShaderParameter("cirrus_pan", _cirrusPan);
        _skyMat.SetShaderParameter("cloud_scale", CloudScale);
        _skyMat.SetShaderParameter("cloud_cover", CloudsEnabled ? _cloudCover : 0f);
        _skyMat.SetShaderParameter("cloud_height", CloudHeight);
        _skyMat.SetShaderParameter("cloud_density", CloudsEnabled ? CloudDensity : 0f);
        _skyMat.SetShaderParameter("cloud_lit", lit);
        _skyMat.SetShaderParameter("cloud_dark", dark);
        _skyMat.SetShaderParameter("cirrus_amount", CloudsEnabled ? CirrusAmount * (1f - overcast * 0.8f) : 0f);
        _skyMat.SetShaderParameter("sun_dir", _sunDir);

        return 1f - SunCloudInfluence * _sunBlock;
    }

    public float CloudShadowAt(Vector3 world)
    {
        if (!CloudsEnabled) return 1f;
        var hit = new Vector2(world.X, world.Z) + CloudField.RayStep(_sunDir) * Mathf.Max(CloudHeight - world.Y, 0f);
        return 1f - CloudShadowStrength * CloudField.CoverAt(hit, _cloudPan, CloudScale, _cloudCover);
    }

}
