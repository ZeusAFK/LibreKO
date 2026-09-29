using Godot;
using Xunit;

namespace LibreKO.Tests;

public class FxSpawnBoxTests
{
    private const float Tolerance = 1e-5f;

    [Fact]
    public void AnUpwardEmitterLaysItsFlatBoxOnTheGround()
    {
        var spawned = FxSpawnBox.Orientation(Vector3.Up) * new Vector3(0.5f, 0.5f, 0f);

        Assert.Equal(0f, spawned.Y, Tolerance);
        Assert.Equal(0.5f, Mathf.Abs(spawned.X), Tolerance);
        Assert.Equal(0.5f, Mathf.Abs(spawned.Z), Tolerance);
    }

    [Fact]
    public void AnEmitterAlongZKeepsItsBox()
    {
        var point = new Vector3(0.3f, -0.2f, 0.1f);

        Assert.Equal(point, FxSpawnBox.Orientation(Vector3.Back) * point);
        Assert.Equal(point, FxSpawnBox.Orientation(Vector3.Zero) * point);
    }

    [Fact]
    public void AnEmitterAgainstZMirrorsItsBoxThroughTheCentre()
    {
        var point = new Vector3(0.3f, -0.2f, 0.1f);

        Assert.Equal(-point, FxSpawnBox.Orientation(Vector3.Forward) * point);
    }

    [Fact]
    public void ADownwardEmitterStandsAGroundBoxUpright()
    {
        var spawned = FxSpawnBox.Orientation(Vector3.Down) * new Vector3(3f, 0f, 3f);

        Assert.Equal(0f, spawned.Z, Tolerance);
        Assert.Equal(3f, Mathf.Abs(spawned.Y), Tolerance);
    }
}
