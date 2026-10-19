using Godot;

namespace LibreKO;

public static class FxLod
{
    private const float NearDistance = 15f;
    private const float FarDistance = 30f;
    private const float FarRatio = 1f / 3f;
    private const float LoadFloor = 0.35f;
    private const float LoadDropPerSecond = 0.5f;
    private const float LoadRisePerSecond = 0.2f;
    private const float StrainedShare = 0.9f;
    private const float EasyShare = 0.97f;
    private const double FrameSmoothing = 0.1;
    private const int DefaultTargetFps = 60;

    private const ulong StaleFrames = 2;

    private static Vector3 camera;
    private static ulong updatedFrame;
    private static double smoothedFrame = 1.0 / DefaultTargetFps;

    public static float Load { get; private set; } = 1f;

    public static void Update(Vector3 camera, double delta)
    {
        FxLod.camera = camera;
        updatedFrame = Engine.GetProcessFrames();
        if (delta <= 0) return;
        smoothedFrame += (delta - smoothedFrame) * FrameSmoothing;
        double fps = 1.0 / smoothedFrame;
        double targetFps = TargetFps(Engine.MaxFps);
        if (fps < targetFps * StrainedShare)
            Load = Mathf.Max(LoadFloor, Load - LoadDropPerSecond * (float)delta);
        else if (fps > targetFps * EasyShare)
            Load = Mathf.Min(1f, Load + LoadRisePerSecond * (float)delta);
    }

    public static int TargetFps(int frameCap) =>
        frameCap > 0 ? Mathf.Min(frameCap, DefaultTargetFps) : DefaultTargetFps;

    public static float Ratio(Vector3 at)
    {
        if (Engine.GetProcessFrames() - updatedFrame > StaleFrames) return 1f;
        float distance = at.DistanceTo(camera);
        float near = distance <= NearDistance ? 1f
            : distance >= FarDistance ? FarRatio
            : Mathf.Lerp(1f, FarRatio, (distance - NearDistance) / (FarDistance - NearDistance));
        return near * Load;
    }

    public static int Thin(int want, float ratio, ref float carry, bool keepOne)
    {
        if (want <= 0 || ratio >= 1f) return want;
        carry += want * ratio;
        int kept = (int)carry;
        carry -= kept;
        return keepOne && kept == 0 ? 1 : kept;
    }
}
