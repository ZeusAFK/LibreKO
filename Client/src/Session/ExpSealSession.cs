namespace LibreKO.Session;

public sealed class ExpSealSession
{
    public event Action<ExpSealReply>? ExpSealEvent;

    internal void RaiseExpSeal(ExpSealReply expSealReply) => ExpSealEvent?.Invoke(expSealReply);

    public ExpSealState ExpSealState { get; private set; }

    public bool On => ExpSealState == ExpSealState.On;

    public long SealedExperience { get; private set; }

    public long SealedGain { get; private set; }

    internal void ApplyReply(ExpSealReply reply)
    {
        SealedGain = 0;
        switch (reply.Sub)
        {
            case ExpSealSubOpcode.On when reply.Code == ExpSealResult.Succeeded:
                ExpSealState = ExpSealState.On;
                break;
            case ExpSealSubOpcode.Off when reply.Code == ExpSealResult.Succeeded:
                ExpSealState = ExpSealState.Off;
                break;
            case ExpSealSubOpcode.Store when reply.Code == ExpSealResult.Succeeded:
                SealedExperience = reply.Total;
                break;
            case ExpSealSubOpcode.Total:
                SealedGain = reply.Total - SealedExperience;
                SealedExperience = reply.Total;
                break;
        }
        RaiseExpSeal(reply);
    }

    internal void Reset(long sealedTotal)
    {
        ExpSealState = ExpSealState.Unknown;
        SealedExperience = sealedTotal;
        SealedGain = 0;
    }
}
