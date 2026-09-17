namespace LibreKO.Domain;

public static class ActionClip
{
    public const double DefaultBlend = 0.1;
    public const double MissingClip = 0.5;
    public const double MinPoseHold = 0.2;

    private const double SingleFrame = 1.0 / 30.0;
    private const double PoseBlendInOut = 2.0;

    public static bool IsStaticPose(double clipLength) => clipLength <= SingleFrame;

    public static double Hold(double clipLength, double blend) =>
        IsStaticPose(clipLength) ? System.Math.Max(MinPoseHold, blend * PoseBlendInOut)
                                 : clipLength;
}
