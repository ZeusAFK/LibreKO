namespace LibreKO.Session;

public sealed class ChatSession
{
    public event Action<bool, string, bool>? ClanConsultationEvent;

    internal void RaiseClanConsultation(bool arg1, string arg2, bool arg3) => ClanConsultationEvent?.Invoke(arg1, arg2, arg3);

    public event Action<ChatLine>? ChatEvent;

    internal void RaiseChat(ChatLine chatLine) => ChatEvent?.Invoke(chatLine);

    public event Action<DeathNotice>? DeathNoticeEvent;

    internal void RaiseDeathNotice(DeathNotice deathNotice) => DeathNoticeEvent?.Invoke(deathNotice);

    public event Action<int>? KrowazNoticeEvent;

    internal void RaiseKrowazNotice(int value) => KrowazNoticeEvent?.Invoke(value);

    public event Action<MonumentNotice>? MonumentNoticeEvent;

    internal void RaiseMonumentNotice(MonumentNotice monumentNotice) => MonumentNoticeEvent?.Invoke(monumentNotice);

    public event Action<ChaosStoneNotice>? ChaosStoneNoticeEvent;

    internal void RaiseChaosStoneNotice(ChaosStoneNotice chaosStoneNotice) => ChaosStoneNoticeEvent?.Invoke(chaosStoneNotice);

    public event Action<BoxItemNotice>? BoxItemNoticeEvent;

    internal void RaiseBoxItemNotice(BoxItemNotice boxItemNotice) => BoxItemNoticeEvent?.Invoke(boxItemNotice);

    public event Action<ChatTargetResult, string>? ChatTargetEvent;

    internal void RaiseChatTarget(ChatTargetResult arg1, string arg2) => ChatTargetEvent?.Invoke(arg1, arg2);

    public bool WhispersBlocked { get; private set; }

    internal void ApplyWhispersBlocked(bool blocked) => WhispersBlocked = blocked;
}
