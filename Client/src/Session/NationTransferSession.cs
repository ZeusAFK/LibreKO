namespace LibreKO.Session;

public sealed class NationTransferSession
{
    public bool Pending { get; internal set; }

    public int Target { get; private set; }

    public IReadOnlyList<NationTransferCandidate> Candidates { get; private set; } = Array.Empty<NationTransferCandidate>();

    internal void Clear()
    {
        Pending = false;
        Target = 0;
        Candidates = Array.Empty<NationTransferCandidate>();
    }

    public event Action<IReadOnlyList<NationTransferCandidate>>? NationTransferOpenEvent;

    private void RaiseNationTransferOpen(IReadOnlyList<NationTransferCandidate> iReadOnlyList) => NationTransferOpenEvent?.Invoke(iReadOnlyList);

    public event Action<int>? NationTransferRefusedEvent;

    private void RaiseNationTransferRefused(int value) => NationTransferRefusedEvent?.Invoke(value);

    public event Action? NationTransferDoneEvent;

    internal void RaiseNationTransferDone() => NationTransferDoneEvent?.Invoke();

    public event Action<int, int>? NationTransferWarEvent;

    private void RaiseNationTransferWar(int arg1, int arg2) => NationTransferWarEvent?.Invoke(arg1, arg2);

    public event Action? NationTransferResetEvent;

    internal void RaiseNationTransferReset() => NationTransferResetEvent?.Invoke();

    internal void ApplyCandidates(IReadOnlyList<NationTransferCandidate> candidates)
    {
        Candidates = candidates;
        Target = candidates.Count == 0 ? 0 : candidates[0].Nation;
        RaiseNationTransferOpen(candidates);
    }

    internal void ApplyWar(int ownWar, int targetWar)
    {
        Pending = false;
        RaiseNationTransferWar(ownWar, targetWar);
    }

    internal void ApplyRefused(int textId)
    {
        Pending = false;
        RaiseNationTransferRefused(textId);
    }

    internal void CancelPending()
    {
        bool hadOperation = Pending || Target != 0;
        Clear();
        if (hadOperation) RaiseNationTransferReset();
    }
}
