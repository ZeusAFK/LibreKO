namespace LibreKO.Session;

public sealed class BountySession
{
    public event Action<List<BountyEntry>>? BountyListEvent;

    internal void RaiseBountyList(List<BountyEntry> list) => BountyListEvent?.Invoke(list);

    public event Action<bool>? BountyPostEvent;

    internal void RaiseBountyPost(bool value) => BountyPostEvent?.Invoke(value);

    public event Action<BountyOutcome>? BountyClaimEvent;

    internal void RaiseBountyClaim(BountyOutcome outcome) => BountyClaimEvent?.Invoke(outcome);

    public event Action<BountyOutcome>? BountyWithdrawEvent;

    internal void RaiseBountyWithdraw(BountyOutcome outcome) => BountyWithdrawEvent?.Invoke(outcome);
}
