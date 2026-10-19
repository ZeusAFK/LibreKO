namespace LibreKO.Domain;

public sealed class RedistributionRequest
{
    public const byte NoKind = 0;

    public ResetKind Kind { get; private set; } = (ResetKind)NoKind;
    public bool AwaitingCost { get; private set; }

    public bool Pending => Kind != (ResetKind)NoKind;
    public bool CanConfirm => Pending && !AwaitingCost;

    public bool TryBegin(byte kind)
    {
        if (Pending || kind == NoKind) return false;
        Kind = (ResetKind)kind;
        AwaitingCost = true;
        return true;
    }

    public bool TakeCost()
    {
        if (!AwaitingCost) return false;
        AwaitingCost = false;
        return true;
    }

    public void Clear()
    {
        Kind = (ResetKind)NoKind;
        AwaitingCost = false;
    }
}
