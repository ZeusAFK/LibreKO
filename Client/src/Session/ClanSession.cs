namespace LibreKO.Session;

public sealed class ClanSession
{
    public event Action<byte, string>? ClanMemoEvent;

    internal void RaiseClanMemo(byte arg1, string arg2) => ClanMemoEvent?.Invoke(arg1, arg2);

    public const int NoClanBonus = -1;

    public int ClanOnlineCount { get; private set; } = NoClanBonus;

    internal void ForgetOnlineCount() => ClanOnlineCount = NoClanBonus;

    public event System.Action<int>? ClanBonusEvent;

    private void RaiseClanBonus(int value) => ClanBonusEvent?.Invoke(value);

    public MyClanInfo MyClan { get; private set; }

    public event Action<MyClanInfo>? MyClanChangedEvent;

    internal void RaiseMyClanChanged(MyClanInfo myClanInfo) => MyClanChangedEvent?.Invoke(myClanInfo);

    public event Action<List<ClanBrowseEntry>>? ClanListEvent;

    internal void RaiseClanList(List<ClanBrowseEntry> list) => ClanListEvent?.Invoke(list);

    public event Action<List<ClanMember>>? ClanMembersEvent;

    internal void RaiseClanMembers(List<ClanMember> list) => ClanMembersEvent?.Invoke(list);

    public event Action<bool, KnightsResult, string>? ClanCreateEvent;

    internal void RaiseClanCreate(bool arg1, KnightsResult arg2, string arg3) => ClanCreateEvent?.Invoke(arg1, arg2, arg3);

    public event Action<EntityClanChange>? EntityClanEvent;

    internal void RaiseEntityClan(EntityClanChange change) => EntityClanEvent?.Invoke(change);

    public event Action<int>? EntityClanClearedEvent;

    internal void RaiseEntityClanCleared(int value) => EntityClanClearedEvent?.Invoke(value);

    public event Action<int, int, byte>? ClanFameEvent;

    internal void RaiseClanFame(int arg1, int arg2, byte arg3) => ClanFameEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int, byte>? AuthorityFameEvent;

    internal void RaiseAuthorityFame(int arg1, byte arg2) => AuthorityFameEvent?.Invoke(arg1, arg2);

    public event Action<ClanMatchPair>? ClanMatchPairEvent;

    private void RaiseClanMatchPair(ClanMatchPair clanMatchPair) => ClanMatchPairEvent?.Invoke(clanMatchPair);

    public ClanMatchPair? MatchPair { get; private set; }

    internal void SeedMyClan(MyInfo info)
    {
        MyClan = info.KnightsId > 0
            ? new MyClanInfo
            {
                InClan = true,
                ClanId = info.KnightsId,
                Name = info.ClanName ?? "",
                Flag = info.ClanFlag,
                Fame = info.ClanFame,
                Grade = info.ClanGrade,
                Ranking = info.ClanRanking,
                AllianceId = info.AllianceId,
                MarkVersion = info.MarkVersion,
                Notice = "",
                MaxMembers = ClanTypes.MaxMembers,
            }
            : new MyClanInfo { Notice = "", Name = "", MaxMembers = ClanTypes.MaxMembers };
        RaiseMyClanChanged(MyClan);
    }

    public event Action? RemovedFromClanEvent;

    internal void RaiseRemovedFromClan() => RemovedFromClanEvent?.Invoke();

    public event Action<int, int, string>? ClanInviteEvent;

    internal void RaiseClanInvite(int arg1, int arg2, string arg3) => ClanInviteEvent?.Invoke(arg1, arg2, arg3);

    public event Action<KnightsSubOpcode, KnightsResult>? ClanResultEvent;

    internal void RaiseClanResult(KnightsSubOpcode arg1, KnightsResult arg2) => ClanResultEvent?.Invoke(arg1, arg2);

    public event Action<int>? ClanNoticeRefusedEvent;

    internal void RaiseClanNoticeRefused(int value) => ClanNoticeRefusedEvent?.Invoke(value);

    public event Action<bool, int, int>? ClanPointStatusEvent;

    internal void RaiseClanPointStatus(bool arg1, int arg2, int arg3) => ClanPointStatusEvent?.Invoke(arg1, arg2, arg3);

    public event Action<KnightsDonateResult, int, int, int>? ClanDonateEvent;

    internal void RaiseClanDonate(KnightsDonateResult arg1, int arg2, int arg3, int arg4) => ClanDonateEvent?.Invoke(arg1, arg2, arg3, arg4);

    public event Action<KnightsPointMethodResult, byte>? ClanPointMethodEvent;

    internal void RaiseClanPointMethod(KnightsPointMethodResult arg1, byte arg2) => ClanPointMethodEvent?.Invoke(arg1, arg2);

    public event Action<List<(string Name, int Points)>>? ClanDonationListEvent;

    internal void RaiseClanDonationList(List<(string Name, int Points)> list) => ClanDonationListEvent?.Invoke(list);

    public event Action<List<(string Name, int Points)>>? ClanLeaderPointsEvent;

    internal void RaiseClanLeaderPoints(List<(string Name, int Points)> list) => ClanLeaderPointsEvent?.Invoke(list);

    public event Action<List<(int Nation, int Rank, string Name)>>? ClanTop10Event;

    internal void RaiseClanTop10(List<(int Nation, int Rank, string Name)> list) => ClanTop10Event?.Invoke(list);

    public event Action<KnightsResult, List<string>>? ClanHandoverListEvent;

    internal void RaiseClanHandoverList(KnightsResult arg1, List<string> list) => ClanHandoverListEvent?.Invoke(arg1, list);

    public event Action<bool, string, string>? ClanHandoverEvent;

    internal void RaiseClanHandover(bool arg1, string arg2, string arg3) => ClanHandoverEvent?.Invoke(arg1, arg2, arg3);

    public event Action<ClanCapeChange>? ClanCapeUpdateEvent;

    internal void RaiseClanCapeUpdate(ClanCapeChange change) => ClanCapeUpdateEvent?.Invoke(change);

    public event Action<List<ClanStanding>>? ClanStandingEvent;

    internal void RaiseClanStanding(List<ClanStanding> list) => ClanStandingEvent?.Invoke(list);

    public event Action<string, bool>? ClanMemberPresenceEvent;

    internal void RaiseClanMemberPresence(string arg1, bool arg2) => ClanMemberPresenceEvent?.Invoke(arg1, arg2);

    public event Action? ClanCapeNpcEvent;

    internal void RaiseClanCapeNpc() => ClanCapeNpcEvent?.Invoke();

    public event Action<string, int>? AllianceInviteEvent;

    internal void RaiseAllianceInvite(string arg1, int arg2) => AllianceInviteEvent?.Invoke(arg1, arg2);

    public event Action<KnightsResult, int>? ClanMarkRegisterEvent;

    internal void RaiseClanMarkRegister(KnightsResult arg1, int arg2) => ClanMarkRegisterEvent?.Invoke(arg1, arg2);

    public event Action<int, int, byte[]>? ClanMarkEvent;

    internal void RaiseClanMark(int arg1, int arg2, byte[] arg3) => ClanMarkEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int, int, bool>? AllianceMembershipEvent;

    internal void RaiseAllianceMembership(int arg1, int arg2, bool arg3) => AllianceMembershipEvent?.Invoke(arg1, arg2, arg3);

    public event Action<string, List<AllianceClanEntry>>? AllianceListEvent;

    internal void RaiseAllianceList(string arg1, List<AllianceClanEntry> list) => AllianceListEvent?.Invoke(arg1, list);

    public void SeedPreview(MyClanInfo clan)
    {
        MyClan = clan;
        RaiseMyClanChanged(MyClan);
    }

    internal void ApplyMyClan(MyClanInfo clan)
    {
        MyClan = clan;
        RaiseMyClanChanged(clan);
    }

    internal void ApplyMarkVersion(int version)
    {
        var mine = MyClan;
        mine.MarkVersion = version;
        MyClan = mine;
    }

    internal void ApplyMatchPair(ClanMatchPair pair)
    {
        MatchPair = pair;
        RaiseClanMatchPair(pair);
    }

    internal void ApplyOnlineCount(int count)
    {
        ClanOnlineCount = count;
        RaiseClanBonus(count);
    }
}
