namespace LibreKO.Session;

public sealed class PartySession
{
    public const int NoCommander = -1;
    private const byte SelfSuccessCode = 1;
    private const string SelfFallbackName = "You";

    private readonly SelfSession self;
    private readonly List<PartyMember> members = new();
    private readonly Dictionary<int, HashSet<byte>> status = new();
    private readonly Dictionary<int, IReadOnlyList<int>> buffs = new();
    private readonly Dictionary<int, int> pings = new();
    private readonly Dictionary<int, int> levelPermille = new();

    public PartySession(SelfSession self) => this.self = self;

    public IReadOnlyList<PartyMember> Members => members;

    public bool InParty => members.Count > 0;

    public bool Joining { get; internal set; }

    public int PartyCommanderId { get; private set; } = NoCommander;

    public PartyBoardType PartyType { get; private set; }

    public PartyBoardType PartyBoardMode { get; private set; }

    internal void ApplyPartyType(PartyBoardType type) => PartyType = type;

    internal void ApplyBoardMode(PartyBoardType mode) => PartyBoardMode = mode;

    internal void ApplyCommander(CommanderNamedReply named)
    {
        PartyCommanderId = named.CharId;
        RaisePartyCommander(named);
    }

    public (short Warnings, short Limit)? PendingVoteNotice { get; private set; }

    internal void HoldVoteNotice(short warnings, short limit) => PendingVoteNotice = (warnings, limit);

    internal (short Warnings, short Limit)? TakeVoteNotice()
    {
        var notice = PendingVoteNotice;
        PendingVoteNotice = null;
        return notice;
    }

    public IReadOnlyCollection<byte>? StatusOf(int charId) =>
        status.TryGetValue(charId, out var memberStatus) ? memberStatus : null;

    public IReadOnlyList<int> BuffsOf(int charId) =>
        buffs.TryGetValue(charId, out var skillIds) ? skillIds : Array.Empty<int>();

    public int PingOf(int charId) =>
        pings.TryGetValue(charId, out int ms) ? ms : PartySignal.Unknown;

    public int? LevelPermilleOf(int charId) =>
        levelPermille.TryGetValue(charId, out int permille) ? permille : null;

    public bool IsMember(int charId)
    {
        foreach (var member in Members)
            if (member.CharId == charId) return true;
        return false;
    }

    public bool IsLeader(int charId) => members.Count > 0 && members[0].CharId == charId;

    public int IndexOf(int charId) => members.FindIndex(x => x.CharId == charId);

    public string NameOf(int charId)
    {
        int idx = IndexOf(charId);
        return idx >= 0 ? members[idx].Name : "";
    }

    internal void Apply(PartyMember m)
    {
        if (m.CharId == self.MyCharId) Joining = false;
        if (m.BecameLeader)
        {
            members.RemoveAll(x => x.CharId == m.CharId);
            members.Insert(0, m);
        }
        else
        {
            int idx = IndexOf(m.CharId);
            if (idx >= 0) members[idx] = m;
            else members.Add(m);
        }

        if (!Joining && IndexOf(self.MyCharId) < 0)
            members.Insert(0, SelfMember());
    }

    internal void Update(int charId, Func<PartyMember, PartyMember> update)
    {
        int idx = IndexOf(charId);
        if (idx >= 0) members[idx] = update(members[idx]);
    }

    internal string Remove(int charId)
    {
        int idx = IndexOf(charId);
        string name = idx >= 0 ? members[idx].Name : "";
        if (idx >= 0) members.RemoveAt(idx);
        status.Remove(charId);
        buffs.Remove(charId);
        pings.Remove(charId);
        levelPermille.Remove(charId);
        if (charId == PartyCommanderId) PartyCommanderId = NoCommander;
        return name;
    }

    public PartyRejoin Rejoin { get; } = new();

    internal void Clear()
    {
        Rejoin.Clear();
        members.Clear();
        status.Clear();
        buffs.Clear();
        pings.Clear();
        levelPermille.Clear();
        Joining = false;
        PartyCommanderId = NoCommander;
    }

    internal void SetStatus(int charId, byte statusType, bool applied)
    {
        if (statusType == 0) return;
        if (!status.TryGetValue(charId, out var set))
        {
            set = new HashSet<byte>();
            status[charId] = set;
        }
        if (applied) set.Add(statusType);
        else set.Remove(statusType);
    }

    internal void SetBuffs(int charId, IReadOnlyList<int> skillIds) => buffs[charId] = skillIds;

    internal void SetPing(int charId, int ms) => pings[charId] = ms;

    internal void SetLevelPermille(int charId, int permille) => levelPermille[charId] = permille;

    public void SeedPreview(params PartyMember[] previewMembers)
    {
        members.Clear();
        members.AddRange(previewMembers);
    }

    public void SeedMemberPreview(int charId, int pingMs, int permille, IReadOnlyList<int> skillIds)
    {
        pings[charId] = pingMs;
        levelPermille[charId] = permille;
        buffs[charId] = skillIds;
    }

    public void SeedCommanderPreview(int charId) => PartyCommanderId = charId;

    private PartyMember SelfMember() =>
        new(self.MyCharId, SelfSuccessCode, self.LastEnter.Name is { Length: > 0 } name ? name : SelfFallbackName,
            self.Vitals.MaxHp, self.Vitals.Hp, self.Sheet.Level, self.LastEnter.Class, self.Vitals.MaxMp, self.Vitals.Mp);

    public event Action<PartyMember>? PartyMemberEvent;

    internal void RaisePartyMember(PartyMember partyMember) => PartyMemberEvent?.Invoke(partyMember);

    public event Action<int>? PartyErrorEvent;

    internal void RaisePartyError(int value) => PartyErrorEvent?.Invoke(value);

    public event Action<int, string>? PartyInviteEvent;

    internal void RaisePartyInvite(int arg1, string arg2) => PartyInviteEvent?.Invoke(arg1, arg2);

    public event Action<int, string>? PartyRemovedEvent;

    internal void RaisePartyRemoved(int arg1, string arg2) => PartyRemovedEvent?.Invoke(arg1, arg2);

    public event Action<PartyMemberVitals>? PartyMemberStatsEvent;

    internal void RaisePartyMemberStats(PartyMemberVitals vitals) => PartyMemberStatsEvent?.Invoke(vitals);

    public event Action<int, int>? PartyMemberLevelEvent;

    internal void RaisePartyMemberLevel(int arg1, int arg2) => PartyMemberLevelEvent?.Invoke(arg1, arg2);

    public event Action<int, int>? PartyMemberClassEvent;

    internal void RaisePartyMemberClass(int arg1, int arg2) => PartyMemberClassEvent?.Invoke(arg1, arg2);

    public event Action<int, byte, bool>? PartyMemberStatusEvent;

    internal void RaisePartyMemberStatus(int arg1, byte arg2, bool arg3) => PartyMemberStatusEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int, IReadOnlyList<int>>? PartyMemberBuffsEvent;

    internal void RaisePartyMemberBuffs(int arg1, IReadOnlyList<int> iReadOnlyList) => PartyMemberBuffsEvent?.Invoke(arg1, iReadOnlyList);

    public event Action? PartySignalEvent;

    internal void RaisePartySignal() => PartySignalEvent?.Invoke();

    public event Action<int, int>? PartyLevelProgressEvent;

    internal void RaisePartyLevelProgress(int arg1, int arg2) => PartyLevelProgressEvent?.Invoke(arg1, arg2);

    public event Action? PartyDisbandEvent;

    internal void RaisePartyDisband() => PartyDisbandEvent?.Invoke();

    public event Action<UserVoteAsk>? UserVoteAskEvent;

    internal void RaiseUserVoteAsk(UserVoteAsk userVoteAsk) => UserVoteAskEvent?.Invoke(userVoteAsk);

    public event Action<short>? UserVoteRefusedEvent;

    internal void RaiseUserVoteRefused(short value) => UserVoteRefusedEvent?.Invoke(value);

    public event Action<UserVoteOutcome>? UserVoteResultEvent;

    internal void RaiseUserVoteResult(UserVoteOutcome userVoteOutcome) => UserVoteResultEvent?.Invoke(userVoteOutcome);

    public event Action<CommanderNamedReply>? PartyCommanderEvent;

    internal void RaisePartyCommander(CommanderNamedReply commanderNamedReply) => PartyCommanderEvent?.Invoke(commanderNamedReply);

    public event Action<CommanderMarkReply>? CommanderMarkEvent;

    internal void RaiseCommanderMark(CommanderMarkReply commanderMarkReply) => CommanderMarkEvent?.Invoke(commanderMarkReply);

    public event Action<byte>? CommanderOrderEvent;

    internal void RaiseCommanderOrder(byte value) => CommanderOrderEvent?.Invoke(value);

    public event Action<bool>? PartyBbsRegisterEvent;

    internal void RaisePartyBbsRegister(bool value) => PartyBbsRegisterEvent?.Invoke(value);

    public event Action? PartyBbsDeleteEvent;

    internal void RaisePartyBbsDelete() => PartyBbsDeleteEvent?.Invoke();

    public event Action<int, int, List<PartyBbsEntry>>? PartyBbsListEvent;

    internal void RaisePartyBbsList(int arg1, int arg2, List<PartyBbsEntry> list) => PartyBbsListEvent?.Invoke(arg1, arg2, list);

    public event Action? PartyBbsWantedFailEvent;

    internal void RaisePartyBbsWantedFail() => PartyBbsWantedFailEvent?.Invoke();

    public event Action<PartyBoardInvite>? PartyBbsInviteEvent;

    internal void RaisePartyBbsInvite(PartyBoardInvite partyBoardInvite) => PartyBbsInviteEvent?.Invoke(partyBoardInvite);
}
