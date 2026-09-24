namespace LibreKO.Domain;

public enum CastInterruptAction
{
    Ignore,
    Cancel,
    ReleaseEarly,
}

public static class CastInterrupt
{
    public static CastInterruptAction OnMove(bool hasCastPhase, bool released, bool rangedDraw, bool pastRangedCommit)
    {
        if (!hasCastPhase || released) return CastInterruptAction.Ignore;
        return rangedDraw && pastRangedCommit ? CastInterruptAction.ReleaseEarly : CastInterruptAction.Cancel;
    }
}
