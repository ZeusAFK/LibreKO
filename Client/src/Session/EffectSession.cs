namespace LibreKO.Session;

public sealed class EffectSession
{
    public event Action<EffectReply>? EffectEvent;

    internal void RaiseEffect(EffectReply effectReply) => EffectEvent?.Invoke(effectReply);
}
