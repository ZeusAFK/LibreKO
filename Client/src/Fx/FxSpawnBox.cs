using Godot;

namespace LibreKO;

public static class FxSpawnBox
{
    private const float Parallel = 0.9999f;
    private const float NoDirection = 1e-12f;

    public static Basis Orientation(Vector3 emitDir)
    {
        if (emitDir.LengthSquared() < NoDirection) return Basis.Identity;
        Vector3 dir = emitDir.Normalized();
        float dot = Vector3.Back.Dot(dir);
        if (dot > Parallel) return Basis.Identity;
        if (dot < -Parallel) return Basis.FromScale(-Vector3.One);
        return new Basis(new Quaternion(Vector3.Back, dir));
    }
}
