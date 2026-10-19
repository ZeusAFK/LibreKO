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

    public Vector3 MoonDir => moonDir;
    public Vector3 SunDir => sunDir;

    private Vector2 cloudPan;
    private Vector2 cirrusPan;
    private float cloudCover = 0.45f;
    private float coverTarget = 0.45f;
    private bool coverInit;
    private float sunBlock;
    private float moonBlock;
    private float moonLit;
    private Vector3 sunDir = Vector3.Up;
    private Vector3 moonDir = Vector3.Down;
    private Vector3 followPos;
    private ShaderMaterial? skyTexOn;

    // At 16 u/s a cloud crossed the sun every ~40s and every shadow pulsed with it.
    private static float BaseWindSpeed(Weather weather) => weather switch
    {
        Weather.Windy => 11f,
        Weather.Rainy => 14f,
        Weather.Snow => 5.5f,
        _ => 4f,
    };

    private static float CoverFor(Weather weather) => weather switch
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
        float speed = BaseWindSpeed(lastWeather) * WindSpeed;
        cloudPan += wind * speed * dt;
        cirrusPan += wind * speed * 2.4f * dt;

        cloudPan = cloudPan.PosMod(1f / Mathf.Max(CloudScale, 1e-6f));
        cirrusPan = cirrusPan.PosMod(1f / Mathf.Max(CloudScale * 0.4f, 1e-6f));

        coverTarget = Mathf.Clamp(CoverFor(lastWeather) + CloudBias, 0f, 1f);
        if (coverInit) cloudCover = Mathf.MoveToward(cloudCover, coverTarget, dt * 0.12f);
        else { cloudCover = coverTarget; coverInit = true; }

        float k = 1f - Mathf.Exp(-dt * 1.2f);
        float sunTarget = CloudsEnabled && sunDir.Y > 0f
            ? CloudField.CoverAt(CloudField.DeckHit(followPos, sunDir, CloudHeight), cloudPan, CloudScale, cloudCover)
            : 0f;
        sunBlock = Mathf.Lerp(sunBlock, sunTarget, k);

        float moonTarget = CloudsEnabled && moonLit > 0.002f
            ? CloudField.CoverAt(CloudField.DeckHit(followPos, moonDir, CloudHeight), cloudPan, CloudScale, cloudCover)
            : 0f;
        moonBlock = Mathf.Lerp(moonBlock, moonTarget, k);
    }

    private float ApplyClouds(float day, float amb, float overcast, Color sunLight, Color horizon)
    {
        var litDay = sunLight.Lerp(Colors.White, 0.35f) * Mathf.Lerp(0.62f, 1.0f, day);
        var darkDay = horizon.Lerp(new Color(0.44f, 0.48f, 0.56f), 0.55f) * Mathf.Lerp(0.55f, 0.88f, day);
        var lit = new Color(0.07f, 0.09f, 0.14f).Lerp(litDay, amb);
        var dark = new Color(0.02f, 0.03f, 0.05f).Lerp(darkDay, amb);
        if (storm)
        {
            lit = lit.Lerp(new Color(0.26f, 0.27f, 0.31f), 0.85f);
            dark = dark.Lerp(new Color(0.08f, 0.09f, 0.12f), 0.85f);
        }

        if (!ReferenceEquals(skyMat, skyTexOn))
        {
            skyMat.SetShaderParameter(Param.CloudTex, CloudField.Texture);
            skyMat.SetShaderParameter(Param.CloudScale, CloudScale);
            skyMat.SetShaderParameter(Param.CloudHeight, CloudHeight);
            skyTexOn = skyMat;
        }
        skyMat.SetShaderParameter(Param.CloudPan, cloudPan);
        skyMat.SetShaderParameter(Param.CirrusPan, cirrusPan);
        skyMat.SetShaderParameter(Param.CloudCover, CloudsEnabled ? cloudCover : 0f);
        skyMat.SetShaderParameter(Param.CloudDensity, CloudsEnabled ? CloudDensity : 0f);
        skyMat.SetShaderParameter(Param.CloudLit, lit);
        skyMat.SetShaderParameter(Param.CloudDark, dark);
        skyMat.SetShaderParameter(Param.CirrusAmount, CloudsEnabled ? CirrusAmount * (1f - overcast * 0.8f) : 0f);
        skyMat.SetShaderParameter(Param.SunDir, sunDir);

        return 1f - SunCloudInfluence * sunBlock;
    }

}
