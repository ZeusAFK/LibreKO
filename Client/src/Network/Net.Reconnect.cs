using System;
using Godot;

namespace LibreKO.Network;

public partial class Net
{
    public enum ReconnectPhase { Idle, Silent, Visible, Failed }

    public const int MaxReconnectAttempts = 10;

    private const string WorldScene = "res://scenes/World.tscn";
    private const double SilentReconnectSeconds = 5.0;
    private const double ReconnectAttemptTimeout = 9.0;
    private const double ReconnectRetryDelay = 4.0;
    private const double ReconnectRetryGrowth = 1.5;
    private const double ReconnectRetryCap = 30.0;
    private const double ReconnectRetryJitter = 0.15;
    private const double StableSessionSeconds = 30.0;
    private const int MissedPingsBeforeDrop = 3;

    public event Action? ReconnectChangedEvent;
    public event Action? ReconnectedEvent;

    public bool AutoReconnect { get; set; } = true;
    public ReconnectPhase ReconnectState { get; private set; }
    public int ReconnectAttempt { get; private set; }
    public string ReconnectFailure { get; private set; } = "";
    public bool ReconnectBlocking => ReconnectState is ReconnectPhase.Visible or ReconnectPhase.Failed;
    public int ReconnectRetryIn => _attemptInFlight ? 0 : Mathf.CeilToInt((float)Math.Max(0, _retryWait));

    private string _host = "";
    private int _port;
    private double _outageElapsed;
    private double _attemptElapsed;
    private double _retryWait;
    private bool _attemptInFlight;
    private ulong _reconnectOkMs;
    private int _missedPings;

    private bool TakeOverDisconnect()
    {
        if (ReconnectState == ReconnectPhase.Failed)
            return true;
        if (ReconnectState != ReconnectPhase.Idle)
        {
            AttemptFailed();
            return true;
        }
        if (!AutoReconnect || MyCharId == 0 || _host.Length == 0
            || _account.Length == 0 || SelectedChar.Length == 0)
            return false;

        if (_reconnectOkMs == 0
            || Time.GetTicksMsec() - _reconnectOkMs > (ulong)(StableSessionSeconds * 1000))
            ReconnectAttempt = 0;

        GD.Print($"[net] connection lost: {_conn.LastError ?? "no reason given"}");
        ReconnectState = ReconnectPhase.Silent;
        ReconnectFailure = "";
        _outageElapsed = 0;
        StartAttempt();
        return true;
    }

    public void CancelReconnect()
    {
        if (ReconnectState == ReconnectPhase.Idle) return;
        ReconnectState = ReconnectPhase.Idle;
        ReconnectFailure = "";
        _attemptInFlight = false;
        ReconnectChangedEvent?.Invoke();
    }

    private void StartAttempt()
    {
        ReconnectAttempt++;
        _attemptElapsed = 0;
        _attemptInFlight = true;
        _connectedFired = false;
        _connectFailReported = false;
        _expectedClose = false;
        _autoLoginPending = true;
        _selectRetries = 0;
        _reconnectKickSent = false;
        _pingOutstanding = false;
        _pingAccum = 0;
        _missedPings = 0;
        PingMs = -1;
        _conn.Connect(_host, _port);
        ReconnectChangedEvent?.Invoke();
    }

    private void AttemptFailed()
    {
        if (ReconnectState is ReconnectPhase.Idle or ReconnectPhase.Failed) return;
        _attemptInFlight = false;
        _conn.Close();
        _connectedFired = false;
        if (ReconnectAttempt >= MaxReconnectAttempts)
        {
            GiveUpReconnect($"No answer from the server after {MaxReconnectAttempts} attempts.");
            return;
        }
        _retryWait = NextRetryDelay();
        GD.Print($"[net] reconnect attempt {ReconnectAttempt} failed; retrying in {_retryWait:0.0}s");
        ReconnectChangedEvent?.Invoke();
    }

    private double NextRetryDelay()
    {
        double grown = ReconnectRetryDelay * Math.Pow(ReconnectRetryGrowth, Math.Max(0, ReconnectAttempt - 1));
        double capped = Math.Min(grown, ReconnectRetryCap);
        return capped * (1.0 + (GD.Randf() * 2.0 - 1.0) * ReconnectRetryJitter);
    }

    private void GiveUpReconnect(string reason)
    {
        GD.Print($"[net] reconnect gave up: {reason}");
        ReconnectState = ReconnectPhase.Failed;
        ReconnectFailure = reason;
        _attemptInFlight = false;
        _conn.Close();
        _connectedFired = false;
        ReconnectChangedEvent?.Invoke();
    }

    private void ReconnectTick(double delta)
    {
        if (ReconnectState is ReconnectPhase.Idle or ReconnectPhase.Failed) return;

        _outageElapsed += delta;
        if (ReconnectState == ReconnectPhase.Silent && _outageElapsed >= SilentReconnectSeconds)
        {
            ReconnectState = ReconnectPhase.Visible;
            ReconnectChangedEvent?.Invoke();
        }

        if (_attemptInFlight)
        {
            _attemptElapsed += delta;
            if (_attemptElapsed >= ReconnectAttemptTimeout) AttemptFailed();
            return;
        }

        _retryWait -= delta;
        if (_retryWait <= 0) StartAttempt();
    }

    private bool ReconnectLogin(bool ok)
    {
        if (ReconnectState is ReconnectPhase.Idle or ReconnectPhase.Failed) return false;
        if (!ok)
        {
            GiveUpReconnect("The server refused the login.");
            return true;
        }
        SelectChar(SelectedChar);
        return true;
    }

    private bool ReconnectSelectFailed()
    {
        if (ReconnectState is ReconnectPhase.Idle or ReconnectPhase.Failed) return false;
        AttemptFailed();
        return true;
    }

    private bool ReconnectEntered()
    {
        if (ReconnectState is ReconnectPhase.Idle or ReconnectPhase.Failed) return false;
        ReconnectState = ReconnectPhase.Idle;
        _attemptInFlight = false;
        _reconnectOkMs = Time.GetTicksMsec();
        GD.Print($"[net] reconnected on attempt {ReconnectAttempt}; reloading the world");
        ReconnectChangedEvent?.Invoke();
        ReconnectedEvent?.Invoke();
        GetTree().ChangeSceneToFile(WorldScene);
        return true;
    }

    private void PingLost()
    {
        if (++_missedPings < MissedPingsBeforeDrop) return;
        DropConnection("the server stopped responding");
    }

    public void DropConnection(string reason)
    {
        _missedPings = 0;
        _conn.LastError = reason;
        _conn.Close();
    }
}
