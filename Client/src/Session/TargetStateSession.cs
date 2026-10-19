namespace LibreKO.Session;

public sealed class TargetStateSession
{
    public event Action<TargetEffectsReply>? TargetEffectsEvent;

    internal void RaiseTargetEffects(TargetEffectsReply targetEffectsReply) => TargetEffectsEvent?.Invoke(targetEffectsReply);

    public event Action<ControlStateReply>? ControlStateEvent;

    internal void RaiseControlState(ControlStateReply controlStateReply) => ControlStateEvent?.Invoke(controlStateReply);

    public event Action<DropListReply>? DropListEvent;

    internal void RaiseDropList(DropListReply dropListReply) => DropListEvent?.Invoke(dropListReply);
}
