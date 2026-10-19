using Godot;

namespace LibreKO.Features.Entities;

internal sealed class ControlStateFeature : IFeature, ILateTickFeature
{
    private readonly IWorldContext context;
    private readonly TargetStateSession targetState;

    public ControlStateFeature(IWorldContext context, TargetStateSession targetState)
    {
        this.context = context;
        this.targetState = targetState;
    }

    public void LateTick(double delta, double nowSec)
    {
        ControlStateTick();
    }

    private const string StatusFrozenText = "Frozen";
    private const string StatusStunnedText = "Stunned";
    private const string StatusBlindedText = "Blinded";
    private const string StatusSlowedText = "Slowed";
    private const double MillisecondsPerSecond = 1000.0;

    private static readonly Color StatusFrozenColour = new(0.6f, 0.85f, 1f);
    private static readonly Color StatusStunnedColour = new(1f, 0.9f, 0.3f);
    private static readonly Color StatusBlindedColour = new(0.75f, 0.5f, 1f);
    private static readonly Color StatusSlowedColour = new(0.4f, 0.6f, 1f);

    private readonly Dictionary<int, double> until = new();
    private readonly List<int> expired = new();

    public void Build() => targetState.ControlStateEvent += On;

    public void Dispose() => targetState.ControlStateEvent -= On;

    private void On(ControlStateReply reply)
    {
        var (text, colour) = StatusLine(reply.Flags);
        if (text.Length == 0)
        {
            ClearStatus(reply.UnitId);
            return;
        }
        until[reply.UnitId] = context.Now + reply.LongestMs / MillisecondsPerSecond;
        context.Entities.PlateOf(reply.UnitId)?.SetStatus(text, colour);
    }

    private static (string Text, Color Colour) StatusLine(byte flags) =>
        (flags & ControlFlags.Frozen) != 0 ? (StatusFrozenText, StatusFrozenColour)
    : (flags & ControlFlags.Stunned) != 0 ? (StatusStunnedText, StatusStunnedColour)
    : (flags & ControlFlags.Blinded) != 0 ? (StatusBlindedText, StatusBlindedColour)
    : (flags & (ControlFlags.Slowed | ControlFlags.AttackSlowed)) != 0 ? (StatusSlowedText, StatusSlowedColour)
    : ("", Colors.White);

    private void ControlStateTick()
    {
        if (until.Count == 0) return;
        double now = context.Now;
        expired.Clear();
        foreach (var (id, endsAt) in until)
            if (now >= endsAt) expired.Add(id);
        foreach (int id in expired)
            ClearStatus(id);
    }

    private void ClearStatus(int id)
    {
        until.Remove(id);
        context.Entities.PlateOf(id)?.SetStatus("", Colors.White);
    }
}
