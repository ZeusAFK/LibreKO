using Godot;
using Xunit;

namespace LibreKO.Tests;

public class GearAttachTests
{
    private const float Tolerance = 1e-3f;

    [Theory]
    [InlineData(5, 24, 7, 5)]
    [InlineData(12, 24, 7, 12)]
    [InlineData(-1, 24, 7, 7)]
    [InlineData(24, 24, 7, 7)]
    public void ThePlugJointIsUsedWhenThatBoneExists(int joint, int boneCount, int fallback, int bone) =>
        Assert.Equal(bone, GearAttach.Bone(joint, boneCount, fallback));

    [Fact]
    public void AShortForearmBringsTheGripBackToTheWrist()
    {
        var wrist = new Vector3(0.0814f, -0.2459f, -0.0217f);
        var plug = new Vector3(0.12735619f, -0.36540079f, 0.02939617f);

        var seated = GearAttach.SeatOnLimb(plug, wrist);

        Assert.InRange(Along(seated, wrist), wrist.Length() - Tolerance, wrist.Length() + Tolerance);
        Assert.True(Along(plug, wrist) > wrist.Length() + 0.05f);
    }

    [Fact]
    public void AShieldAuthoredPastTheHandSitsOnTheWrist()
    {
        var wrist = new Vector3(-0.0553f, -0.2565f, -0.0100f);
        var plug = new Vector3(-0.08699623f, -0.37293223f, 0.01961478f);

        var seated = GearAttach.SeatOnLimb(plug, wrist);

        Assert.InRange(Along(seated, wrist), wrist.Length() - Tolerance, wrist.Length() + Tolerance);
    }

    [Fact]
    public void AForearmThatAlreadyReachesThePlugIsNotMoved()
    {
        var wrist = new Vector3(0.0830f, -0.3549f, -0.0357f);
        var plug = new Vector3(0.10079921f, -0.34352550f, -0.01123281f);

        Assert.Equal(plug, GearAttach.SeatOnLimb(plug, wrist));
    }

    [Fact]
    public void ABoneWithNoOutgoingLimbKeepsThePlugWhereItWasAuthored()
    {
        var plug = new Vector3(-0.087f, -0.373f, 0.020f);

        Assert.Equal(plug, GearAttach.SeatOnLimb(plug, Vector3.Zero));
    }

    private static float Along(Vector3 point, Vector3 limb) => point.Dot(limb) / limb.Length();
}
