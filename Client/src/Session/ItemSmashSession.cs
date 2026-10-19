namespace LibreKO.Session;

public sealed class ItemSmashSession
{
    public event Action<ItemSmashReply>? ItemSmashReplyEvent;

    internal void RaiseItemSmashReply(ItemSmashReply itemSmashReply) => ItemSmashReplyEvent?.Invoke(itemSmashReply);
}
