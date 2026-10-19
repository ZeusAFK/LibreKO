using Godot;

namespace LibreKO.Network;

public enum ReconnectPhase { Idle, Silent, Visible, Failed }

internal interface IReconnectHost
{
    bool CanResume { get; }
    string? LastError { get; }
    void StartAttempt();
    void CloseConnection();
    void SelectCharacter();
    void ReloadWorld();
}

public sealed class ReconnectSupervisor
{
    public const int MaxAttempts = 10;

    private const double SilentSeconds = 5.0;
    private const double AttemptTimeout = 9.0;
    private const double RetryDelay = 4.0;
    private const double RetryGrowth = 1.5;
    private const double RetryCap = 30.0;
    private const double RetryJitter = 0.15;
    private const double StableSessionSeconds = 30.0;

    private readonly IReconnectHost host;
    private double outageElapsed;
    private double attemptElapsed;
    private double retryWait;
    private bool attemptInFlight;
    private ulong reconnectedAtMs;

    internal ReconnectSupervisor(IReconnectHost host)
    {
        this.host = host;
    }

    public event Action? ChangedEvent;

    public event Action? ReconnectedEvent;

    public bool AutoReconnect { get; set; } = true;

    public ReconnectPhase State { get; private set; }

    public int Attempt { get; private set; }

    public string Failure { get; private set; } = "";

    public bool Blocking => State is ReconnectPhase.Visible or ReconnectPhase.Failed;

    public bool InProgress => State is not (ReconnectPhase.Idle or ReconnectPhase.Failed);

    public int RetryIn => attemptInFlight ? 0 : Mathf.CeilToInt((float)Math.Max(0, retryWait));

    internal bool KickSent { get; set; }

    internal bool TakeOverDisconnect()
    {
        if (State == ReconnectPhase.Failed)
            return true;
        if (State != ReconnectPhase.Idle)
        {
            AttemptFailed();
            return true;
        }
        if (!AutoReconnect || !host.CanResume)
            return false;

        if (reconnectedAtMs == 0
            || Time.GetTicksMsec() - reconnectedAtMs > (ulong)(StableSessionSeconds * 1000))
            Attempt = 0;

        GD.Print($"[net] connection lost: {host.LastError ?? "no reason given"}");
        State = ReconnectPhase.Silent;
        Failure = "";
        outageElapsed = 0;
        StartAttempt();
        return true;
    }

    public void Cancel()
    {
        if (State == ReconnectPhase.Idle) return;
        State = ReconnectPhase.Idle;
        Failure = "";
        attemptInFlight = false;
        ChangedEvent?.Invoke();
    }

    private void StartAttempt()
    {
        Attempt++;
        attemptElapsed = 0;
        attemptInFlight = true;
        KickSent = false;
        host.StartAttempt();
        ChangedEvent?.Invoke();
    }

    internal void AttemptFailed()
    {
        if (!InProgress) return;
        attemptInFlight = false;
        host.CloseConnection();
        if (Attempt >= MaxAttempts)
        {
            GiveUp($"No answer from the server after {MaxAttempts} attempts.");
            return;
        }
        retryWait = NextRetryDelay();
        GD.Print($"[net] reconnect attempt {Attempt} failed; retrying in {retryWait:0.0}s");
        ChangedEvent?.Invoke();
    }

    private double NextRetryDelay()
    {
        double grown = RetryDelay * Math.Pow(RetryGrowth, Math.Max(0, Attempt - 1));
        double capped = Math.Min(grown, RetryCap);
        return capped * (1.0 + (GD.Randf() * 2.0 - 1.0) * RetryJitter);
    }

    internal void GiveUp(string reason)
    {
        GD.Print($"[net] reconnect gave up: {reason}");
        State = ReconnectPhase.Failed;
        Failure = reason;
        attemptInFlight = false;
        host.CloseConnection();
        ChangedEvent?.Invoke();
    }

    internal void Tick(double delta)
    {
        if (!InProgress) return;

        outageElapsed += delta;
        if (State == ReconnectPhase.Silent && outageElapsed >= SilentSeconds)
        {
            State = ReconnectPhase.Visible;
            ChangedEvent?.Invoke();
        }

        if (attemptInFlight)
        {
            attemptElapsed += delta;
            if (attemptElapsed >= AttemptTimeout) AttemptFailed();
            return;
        }

        retryWait -= delta;
        if (retryWait <= 0) StartAttempt();
    }

    internal bool LoggedIn(bool ok)
    {
        if (!InProgress) return false;
        if (!ok)
        {
            GiveUp("The server refused the login.");
            return true;
        }
        host.SelectCharacter();
        return true;
    }

    internal bool SelectFailed()
    {
        if (!InProgress) return false;
        AttemptFailed();
        return true;
    }

    internal bool Entered()
    {
        if (!InProgress) return false;
        State = ReconnectPhase.Idle;
        attemptInFlight = false;
        reconnectedAtMs = Time.GetTicksMsec();
        GD.Print($"[net] reconnected on attempt {Attempt}; reloading the world");
        ChangedEvent?.Invoke();
        ReconnectedEvent?.Invoke();
        host.ReloadWorld();
        return true;
    }
}
