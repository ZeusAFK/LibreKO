using Godot;

namespace LibreKO.Domain;

public sealed class MoveSendClock
{
    public const float SendSeconds = 1.5f;
    public const float LeadSeconds = 1.5f;
    public const float TurnCheckBelow = 1.4f;
    public const float TurnCheckBelowAlternate = 1.17f;
    public const float TurnDot = 0.6f;
    public const float RotateSeconds = 4f;
    public const float ClimbResend = 1f;
    public const float HeadingEpsilonDegrees = 0.05f;

    private float left;
    private float rotateLeft;
    private bool alternate;
    private bool hasSent;
    private Vector2 sentFrom;
    private Vector2 sentTo;
    private float sentY;

    public void Tick(float delta)
    {
        left -= delta;
        rotateLeft -= delta;
    }

    public bool MoveDue(Vector2 position, float y, Vector2 heading)
    {
        if (!hasSent) return true;
        bool moved = position != sentFrom || Mathf.Abs(y - sentY) >= ClimbResend;
        if (left < 0f) return moved;
        if (left >= (alternate ? TurnCheckBelowAlternate : TurnCheckBelow) || !moved) return false;
        var sent = sentTo - sentFrom;
        var sentDir = sent.LengthSquared() > 0f ? sent.Normalized() : Vector2.Zero;
        var headingDir = heading.LengthSquared() > 0f ? heading.Normalized() : Vector2.Zero;
        return sentDir.Dot(headingDir) < TurnDot;
    }

    public void Sent(Vector2 from, Vector2 to, float y)
    {
        left = SendSeconds;
        alternate = !alternate;
        hasSent = true;
        sentFrom = from;
        sentTo = to;
        sentY = y;
    }

    public void Stopped() => hasSent = false;

    public bool RotateDue(float headingDegrees, float lastSentDegrees) =>
        rotateLeft < 0f
        && (float.IsNaN(lastSentDegrees) || Mathf.Abs(Mathf.AngleDifference(
            Mathf.DegToRad(headingDegrees), Mathf.DegToRad(lastSentDegrees))) > Mathf.DegToRad(HeadingEpsilonDegrees));

    public void Rotated() => rotateLeft = RotateSeconds;

    public static Vector2 Lead(Vector2 position, Vector2 facing, float signedSpeed)
    {
        if (facing.LengthSquared() <= 0f) return position;
        return position + facing.Normalized() * signedSpeed * LeadSeconds;
    }
}
