namespace LibreKO.Session;

public sealed class ChallengeSession
{
    public event Action<string>? ChallengeRequestEvent;

    internal void RaiseChallengeRequest(string value) => ChallengeRequestEvent?.Invoke(value);

    public event Action<string>? ChallengeSentEvent;

    internal void RaiseChallengeSent(string value) => ChallengeSentEvent?.Invoke(value);

    public event Action? ChallengeCancelledEvent;

    internal void RaiseChallengeCancelled() => ChallengeCancelledEvent?.Invoke();

    public event Action? ChallengeRejectedEvent;

    internal void RaiseChallengeRejected() => ChallengeRejectedEvent?.Invoke();

    public event Action<ChallengeSubOpcode>? ChallengeErrorEvent;

    internal void RaiseChallengeError(ChallengeSubOpcode value) => ChallengeErrorEvent?.Invoke(value);
}
