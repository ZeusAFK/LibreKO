namespace LibreKO.Session;

public sealed class UserOptionSession
{
    private readonly SelfSession self;
    private readonly HashSet<int> rankAuraHidden = new();

    public UserOptionSession(SelfSession self) => this.self = self;

    public bool OwnRankAuraShown { get; private set; } = true;

    public bool PartyRequestsBlocked { get; private set; }

    internal void BlockPartyRequests(bool blocked) => PartyRequestsBlocked = blocked;

    public bool RankAuraShown(int id) => id == self.MyCharId ? OwnRankAuraShown : !rankAuraHidden.Contains(id);

    internal void SetRankAuraShown(int id, bool shown)
    {
        if (shown) rankAuraHidden.Remove(id);
        else rankAuraHidden.Add(id);
    }

    internal void Forget(int id) => rankAuraHidden.Remove(id);

    internal void Clear() => rankAuraHidden.Clear();

    internal void ResetForWorld()
    {
        rankAuraHidden.Clear();
        OwnRankAuraShown = true;
    }

    internal void ApplyOwnRankAuraShown(bool shown) => OwnRankAuraShown = shown;

    public event Action<int, bool>? RankAuraShownEvent;

    internal void RaiseRankAuraShown(int arg1, bool arg2) => RankAuraShownEvent?.Invoke(arg1, arg2);
}
