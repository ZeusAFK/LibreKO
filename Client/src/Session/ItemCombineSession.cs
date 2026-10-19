namespace LibreKO.Session;

public sealed class ItemCombineSession
{
    public event Action<CombineReply>? ItemCombineReplyEvent;

    internal void RaiseItemCombineReply(CombineReply combineReply) => ItemCombineReplyEvent?.Invoke(combineReply);

    public event Action<CombineEffect>? CombineEffectEvent;

    internal void RaiseCombineEffect(CombineEffect combineEffect) => CombineEffectEvent?.Invoke(combineEffect);
}
