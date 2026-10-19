namespace LibreKO.Session;

public sealed class KillAssistSession
{
    public event Action<KillStreakReply>? KillStreakEvent;

    internal void RaiseKillStreak(KillStreakReply killStreakReply) => KillStreakEvent?.Invoke(killStreakReply);

    public event Action<AssistReply>? AssistEvent;

    internal void RaiseAssist(AssistReply assistReply) => AssistEvent?.Invoke(assistReply);
}
