using Godot;
using Xunit;

namespace LibreKO.Tests;

public class FxSpinTests
{
    private const float Tolerance = 1e-4f;

    private static Vector3 Mirror(Vector3 v) => new(-v.X, v.Y, v.Z);

    private static Vector3 Baked(Vector3 mapRate) => Mirror(mapRate);

    private static Vector3 N3Rotation(Vector3 p, Vector3 angles)
    {
        float sx = Mathf.Sin(angles.X), cx = Mathf.Cos(angles.X);
        float sy = Mathf.Sin(angles.Y), cy = Mathf.Cos(angles.Y);
        float sz = Mathf.Sin(angles.Z), cz = Mathf.Cos(angles.Z);
        var row0 = new Vector3(cz * cy, sz * cy, -sy);
        var row1 = new Vector3(sy * sx * cz - sz * cx, sy * sx * sz + cz * cx, cy * sx);
        var row2 = new Vector3(sy * cx * cz + sz * sx, sy * cx * sz - cz * sx, cy * cx);
        return p.X * row0 + p.Y * row1 + p.Z * row2;
    }

    private static Vector3 D3DRotationY(Vector3 p, float angle)
    {
        float s = Mathf.Sin(angle), c = Mathf.Cos(angle);
        return new Vector3(p.X * c + p.Z * s, p.Y, -p.X * s + p.Z * c);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
        Assert.Equal(expected.Z, actual.Z, Tolerance);
    }

    [Theory]
    [InlineData(1.3f, 0f, 0f)]
    [InlineData(0f, 0.7f, 0f)]
    [InlineData(0f, 0f, -2.1f)]
    [InlineData(0.4f, -1.1f, 0.9f)]
    public void AMeshSpinsAsTheMirroredN3Rotation(float x, float y, float z)
    {
        var mapRate = new Vector3(x, y, z);
        const float age = 0.75f;
        var point = new Vector3(2f, -1f, 3f);

        var turned = FxSpin.MeshOrientation(FxSpin.RateFromBaked(Baked(mapRate)), age) * Mirror(point);

        AssertClose(Mirror(N3Rotation(point, mapRate * age)), turned);
    }

    [Theory]
    [InlineData(0.9f)]
    [InlineData(-2.4f)]
    public void AGroundBoardYawsAsTheMirroredD3DYaw(float mapRate)
    {
        const float age = 1.5f;
        var point = new Vector3(4f, 0f, -1f);
        float yaw = FxSpin.RateFromBaked(Baked(new Vector3(0f, mapRate, 0f))).Y * age;

        var turned = new Basis(Vector3.Up, yaw) * Mirror(point);

        AssertClose(Mirror(D3DRotationY(point, mapRate * age)), turned);
    }

    [Theory]
    [InlineData(5.0f)]
    [InlineData(-0.5f)]
    public void ScreenPlaneRollsKeepTheAuthoredSign(float mapRoll)
    {
        Assert.Equal(mapRoll, FxSpin.RateFromBaked(Baked(new Vector3(mapRoll, 0f, 0f))).X, Tolerance);
        Assert.Equal(mapRoll, FxSpin.RollFromBaked(-mapRoll), Tolerance);
    }
}
