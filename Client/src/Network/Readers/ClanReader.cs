namespace LibreKO.Network.Readers;

internal sealed class ClanReader : IPacketReader
{
    private readonly ClanSession clan;
    private readonly ProgressSession progress;
    private readonly SelfSession self;

    private const byte BrowsePage = 1;
    private const int TopBoardEntries = 10;
    private const int TopBoardPerNation = 5;
    private const byte AuthorityFameSub = 1;
    private const int AuthorityFameBytes = 1 + sizeof(int) + 1;
    private const int MarkRegisterReplyBytes = 4;
    private const int MarkReplyHeaderBytes = 10;

    public ClanReader(ClanSession clan, ProgressSession progress, SelfSession self)
    {
        this.clan = clan;
        this.progress = progress;
        this.self = self;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.OnOtherSubs<KnightsSubOpcode>(GameOpcodes.GS_KNIGHTS_PROCESS, HandleKnights);
        dispatcher.On(GameOpcodes.GS_KNIGHTS_LIST, HandleKnightsList);
        dispatcher.On(GameOpcodes.GS_AUTHORITY_CHANGE, HandleAuthorityChange);
    }

    private void HandleAuthorityChange(Packet p)
    {
        if (p.RemainingBytes < AuthorityFameBytes || p.ReadByte() != AuthorityFameSub) return;
        int charId = p.ReadInt();
        byte fame = p.ReadByte();
        if (charId == self.MyCharId && clan.MyClan.InClan)
        {
            var mine = clan.MyClan;
            mine.Fame = fame;
            clan.ApplyMyClan(mine);
        }
        clan.RaiseAuthorityFame(charId, fame);
    }

    private void ClearMyClan() => clan.ApplyMyClan(new MyClanInfo { Notice = "", Name = "", MaxMembers = ClanTypes.MaxMembers });

    private void HandleClanMarkRegister(Packet p)
    {
        if (p.RemainingBytes < MarkRegisterReplyBytes) return;
        var result = (KnightsResult)p.ReadUShort();
        int version = p.ReadUShort();
        if (result == KnightsResult.Succeeded && clan.MyClan.InClan) clan.ApplyMarkVersion(version);
        clan.RaiseClanMarkRegister(result, version);
    }

    private void HandleClanMark(Packet p)
    {
        if (p.RemainingBytes < MarkReplyHeaderBytes) return;
        var result = (KnightsResult)p.ReadUShort();
        p.ReadUShort();
        int clanId = p.ReadUShort();
        int version = p.ReadUShort();
        int size = p.ReadUShort();
        if (result != KnightsResult.Succeeded || size > p.RemainingBytes) return;
        clan.RaiseClanMark(clanId, version, p.ReadBytes(size));
    }

    private void HandleKnights(Packet p, KnightsSubOpcode sub)
    {
        switch (sub)
        {
            case KnightsSubOpcode.KnightsPair:
                if (ClanMatchReply.ReadPair(p) is { } pair)
                {
                    clan.ApplyMatchPair(pair);
                }
                return;
            case KnightsSubOpcode.MarkRegister: HandleClanMarkRegister(p); break;
            case KnightsSubOpcode.ClanBonus:
                {
                    bool ok = p.RemainingBytes >= 1 && (KnightsResult)p.ReadByte() == KnightsResult.Succeeded;
                    clan.ApplyOnlineCount(ok && p.RemainingBytes >= 1 ? p.ReadByte() : ClanSession.NoClanBonus);
                    break;
                }
            case KnightsSubOpcode.MarkReq: HandleClanMark(p); break;
            case KnightsSubOpcode.Create:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default;
                    if (result != KnightsResult.Succeeded) { clan.RaiseClanCreate(false, result, ""); break; }
                    int charId = p.ReadInt();
                    int clanId = p.ReadShort();
                    string name = p.ReadString();
                    byte grade = p.ReadByte(), ranking = p.ReadByte();
                    int gold = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
                    if (charId == self.MyCharId)
                    {
                        clan.ApplyMyClan(new MyClanInfo
                        {
                            InClan = true,
                            ClanId = clanId,
                            Name = name,
                            Flag = ClanTypes.Training,
                            Fame = ClanRanks.Chief,
                            Grade = grade,
                            Ranking = ranking,
                            Notice = "",
                            MaxMembers = ClanTypes.MaxMembers,
                        });
                        if (gold >= 0) progress.ApplyGold(gold);
                        clan.RaiseClanCreate(true, default, name);
                    }
                    else clan.RaiseEntityClan(new EntityClanChange(CharId: charId, ClanId: clanId, Name: name, Grade: grade, Ranking: ranking, Fame: ClanRanks.Chief, CapeId: -1, Colour: 0));
                    break;
                }
            case KnightsSubOpcode.Join:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default;
                    if (result != KnightsResult.Succeeded) { clan.RaiseClanResult(sub, result); break; }
                    int charId = p.ReadInt();
                    int clanId = p.ReadShort();
                    byte fame = p.ReadByte(), flag = p.ReadByte();
                    int allianceId = p.ReadShort();
                    int capeId = p.ReadShort();
                    int colour = p.ReadInt();
                    int markVersion = p.ReadShort();
                    string name = p.ReadString();
                    byte grade = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    byte ranking = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    if (charId == self.MyCharId)
                    {
                        clan.ApplyMyClan(new MyClanInfo
                        {
                            InClan = true,
                            ClanId = clanId,
                            Name = name,
                            Flag = flag,
                            Fame = fame,
                            Grade = grade,
                            Ranking = ranking,
                            AllianceId = allianceId,
                            MarkVersion = markVersion,
                            Notice = "",
                            MaxMembers = ClanTypes.MaxMembers,
                        });
                        clan.RaiseClanResult(sub, result);
                    }
                    else clan.RaiseEntityClan(new EntityClanChange(CharId: charId, ClanId: clanId, Name: name, Grade: grade, Ranking: ranking, Fame: fame, CapeId: capeId, Colour: colour));
                    break;
                }
            case KnightsSubOpcode.Withdraw:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default;
                    if (result != KnightsResult.Succeeded) { clan.RaiseClanResult(sub, result); break; }
                    int charId = p.RemainingBytes >= 4 ? p.ReadInt() : self.MyCharId;
                    if (charId == self.MyCharId) { ClearMyClan(); clan.RaiseClanResult(sub, result); }
                    else clan.RaiseEntityClanCleared(charId);
                    break;
                }
            case KnightsSubOpcode.Destroy:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default;
                    if (result == KnightsResult.Succeeded) ClearMyClan();
                    clan.RaiseClanResult(sub, result);
                    break;
                }
            case KnightsSubOpcode.Remove:
            case KnightsSubOpcode.Admit:
            case KnightsSubOpcode.Reject:
            case KnightsSubOpcode.Chief:
            case KnightsSubOpcode.Vicechief:
            case KnightsSubOpcode.Officer:
                clan.RaiseClanResult(sub, p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default);
                break;
            case KnightsSubOpcode.AllListRequest:
                {
                    var list = new List<ClanStanding>();
                    int count = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                    {
                        var entry = new ClanStanding { ClanId = p.ReadShort(), Grade = p.ReadByte(), Ranking = p.ReadByte() };
                        list.Add(entry);
                        if (clan.MyClan.InClan && entry.ClanId == clan.MyClan.ClanId)
                        {
                            var mine = clan.MyClan;
                            mine.Grade = entry.Grade;
                            mine.Ranking = entry.Ranking;
                            clan.ApplyMyClan(mine);
                        }
                    }
                    clan.RaiseClanStanding(list);
                    break;
                }
            case KnightsSubOpcode.MemberRequest:
                {
                    var list = new List<ClanMember>();
                    byte page = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    if (page != BrowsePage) { clan.RaiseClanMembers(list); break; }
                    p.ReadShort();
                    int online = p.ReadShort();
                    int max = p.ReadShort();
                    string notice = p.ReadString();
                    int count = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    for (int i = 0; i < count && p.RemainingBytes >= 3; i++)
                    {
                        var m = new ClanMember { Name = p.ReadString() };
                        m.Fame = p.ReadByte();
                        p.ReadSByteString();
                        m.Level = p.ReadByte();
                        m.Class = p.ReadShort();
                        m.IsOnline = p.ReadByte() != 0;
                        m.Memo = p.ReadString();
                        m.HoursSinceLogin = p.ReadInt();
                        list.Add(m);
                    }
                    if (clan.MyClan.InClan)
                    {
                        var mine = clan.MyClan;
                        mine.Notice = notice;
                        mine.Online = online;
                        mine.MaxMembers = max > 0 ? max : ClanTypes.MaxMembers;
                        clan.ApplyMyClan(mine);
                    }
                    clan.RaiseClanMembers(list);
                    break;
                }
            case KnightsSubOpcode.ModifyFame:
                {
                    if ((p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default) != KnightsResult.Succeeded) break;
                    int charId = p.ReadInt();
                    int clanId = p.ReadShort();
                    byte fame = p.ReadByte();
                    if (charId == self.MyCharId)
                    {
                        if (clanId == 0)
                        {
                            bool expelled = clan.MyClan.InClan;
                            ClearMyClan();
                            if (expelled) clan.RaiseRemovedFromClan();
                        }
                        else
                        {
                            var mine = clan.MyClan;
                            mine.Fame = fame;
                            clan.ApplyMyClan(mine);
                        }
                    }
                    clan.RaiseClanFame(charId, clanId, fame);
                    break;
                }
            case KnightsSubOpcode.Invite:
                {
                    if ((p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default) != KnightsResult.Succeeded) break;
                    int inviterId = p.ReadInt();
                    int clanId = p.ReadShort();
                    clan.RaiseClanInvite(inviterId, clanId, p.ReadString());
                    break;
                }
            case KnightsSubOpcode.CapeNpc:
                clan.RaiseClanCapeNpc();
                break;
            case KnightsSubOpcode.AllyCreate:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default;
                    if (result == KnightsResult.Succeeded && clan.MyClan.InClan)
                    {
                        var mine = clan.MyClan;
                        mine.AllianceId = mine.ClanId;
                        clan.ApplyMyClan(mine);
                        clan.RaiseAllianceMembership(mine.ClanId, mine.ClanId, true);
                    }
                    else clan.RaiseClanResult(sub, result);
                    break;
                }
            case KnightsSubOpcode.AllyReq:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default;
                    if (result != KnightsResult.Succeeded) { clan.RaiseClanResult(sub, result); break; }
                    string name = p.ReadSByteString();
                    int clanId = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    clan.RaiseAllianceInvite(name, clanId);
                    break;
                }
            case KnightsSubOpcode.AllyInsert:
                {
                    if ((p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default) != KnightsResult.Succeeded) { clan.RaiseClanResult(sub, default); break; }
                    int mainId = p.ReadShort();
                    int clanId = p.ReadShort();
                    int capeId = p.ReadShort();
                    int colour = p.ReadInt();
                    if (clan.MyClan.InClan && clanId == clan.MyClan.ClanId)
                    {
                        var mine = clan.MyClan;
                        mine.AllianceId = mainId;
                        clan.ApplyMyClan(mine);
                    }
                    clan.RaiseClanCapeUpdate(new ClanCapeChange(ClanId: clanId, CapeId: capeId, R: colour & 0xFF, G: (colour >> 8) & 0xFF, B: (colour >> 16) & 0xFF));
                    clan.RaiseAllianceMembership(mainId, clanId, true);
                    break;
                }
            case KnightsSubOpcode.AllyRemove:
            case KnightsSubOpcode.AllyPunish:
                {
                    if ((p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default) != KnightsResult.Succeeded) { clan.RaiseClanResult(sub, default); break; }
                    int allianceId = p.ReadShort();
                    int clanId = p.ReadShort();
                    int capeId = p.ReadShort();
                    int colour = p.ReadInt();
                    if (clan.MyClan.InClan && (clanId == clan.MyClan.ClanId || allianceId == clan.MyClan.ClanId && clanId == allianceId))
                    {
                        var mine = clan.MyClan;
                        mine.AllianceId = 0;
                        clan.ApplyMyClan(mine);
                    }
                    if (capeId >= 0)
                        clan.RaiseClanCapeUpdate(new ClanCapeChange(ClanId: clanId, CapeId: capeId, R: colour & 0xFF, G: (colour >> 8) & 0xFF, B: (colour >> 16) & 0xFF));
                    clan.RaiseAllianceMembership(allianceId, clanId, false);
                    break;
                }
            case KnightsSubOpcode.AllyList:
                {
                    var clans = new List<AllianceClanEntry>();
                    int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    if (count == 0) { clan.RaiseAllianceList("", clans); break; }
                    string notice = p.ReadString();
                    for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                    {
                        var member = new AllianceClanEntry { Id = p.ReadShort(), Name = p.ReadSByteString(), Officers = new List<AllianceOfficer>() };
                        member.InAlliance = p.ReadByte() != 0;
                        int officers = p.ReadByte();
                        for (int j = 0; j < officers && p.RemainingBytes >= 2; j++)
                            member.Officers.Add(new AllianceOfficer { Fame = p.ReadByte(), Name = p.ReadSByteString() });
                        clans.Add(member);
                    }
                    clan.RaiseAllianceList(notice, clans);
                    break;
                }
            case KnightsSubOpcode.Update:
                {
                    if (p.RemainingBytes < 8) break;
                    int clanId = p.ReadShort();
                    byte flag = p.ReadByte();
                    int capeId = p.ReadShort();
                    int r = p.ReadByte(), g = p.ReadByte(), b = p.ReadByte();
                    p.ReadByte();
                    int fund = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
                    if (clan.MyClan.InClan && clanId == clan.MyClan.ClanId)
                    {
                        var mine = clan.MyClan;
                        mine.Flag = flag;
                        if (fund >= 0) mine.PointFund = fund;
                        clan.ApplyMyClan(mine);
                        self.ChangeCape(capeId, r, g, b);
                    }
                    clan.RaiseClanCapeUpdate(new ClanCapeChange(ClanId: clanId, CapeId: capeId, R: r, G: g, B: b));
                    break;
                }
            case KnightsSubOpcode.MemberOnline:
            case KnightsSubOpcode.MemberOffline:
                clan.RaiseClanMemberPresence(p.ReadSByteString(), sub == KnightsSubOpcode.MemberOnline);
                break;
            case KnightsSubOpcode.PointRequest:
                {
                    bool ok = (p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default) == KnightsResult.Succeeded;
                    int np = ok && p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    int fund = ok && p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    if (ok && clan.MyClan.InClan)
                    {
                        var mine = clan.MyClan;
                        mine.PointFund = fund;
                        clan.ApplyMyClan(mine);
                    }
                    clan.RaiseClanPointStatus(ok, np, fund);
                    break;
                }
            case KnightsSubOpcode.PointMethod:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsPointMethodResult)p.ReadByte() : default;
                    byte method = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                    if (result == KnightsPointMethodResult.Succeeded && clan.MyClan.InClan)
                    {
                        var mine = clan.MyClan;
                        mine.PointMethod = method;
                        clan.ApplyMyClan(mine);
                    }
                    clan.RaiseClanPointMethod(result, method);
                    break;
                }
            case KnightsSubOpcode.DonatePoints:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsDonateResult)p.ReadByte() : default;
                    if (result != KnightsDonateResult.Succeeded) { clan.RaiseClanDonate(result, 0, 0, 0); break; }
                    int np = p.ReadInt();
                    int fund = p.ReadInt();
                    p.ReadByte();
                    int amount = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    if (clan.MyClan.InClan)
                    {
                        var mine = clan.MyClan;
                        mine.PointFund = fund;
                        clan.ApplyMyClan(mine);
                    }
                    clan.RaiseClanDonate(result, np, fund, amount);
                    break;
                }
            case KnightsSubOpcode.HandoverList:
                {
                    var state = p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default;
                    var names = new List<string>();
                    if (state == KnightsResult.Succeeded)
                    {
                        int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                        for (int i = 0; i < count && p.RemainingBytes >= 2; i++) names.Add(p.ReadString());
                    }
                    clan.RaiseClanHandoverList(state, names);
                    break;
                }
            case KnightsSubOpcode.HandoverReq:
                {
                    var result = p.RemainingBytes >= 1 ? (KnightsResult)p.ReadByte() : default;
                    if (result != KnightsResult.Succeeded) { clan.RaiseClanHandover(false, "", ""); break; }
                    string oldChief = p.ReadString();
                    string newChief = p.ReadString();
                    if (clan.MyClan.InClan)
                    {
                        var mine = clan.MyClan;
                        if (string.Equals(newChief, self.LastEnter.Name, StringComparison.OrdinalIgnoreCase)) mine.Fame = ClanRanks.Chief;
                        else if (string.Equals(oldChief, self.LastEnter.Name, StringComparison.OrdinalIgnoreCase)) mine.Fame = ClanRanks.Trainee;
                        clan.ApplyMyClan(mine);
                    }
                    clan.RaiseClanHandover(true, oldChief, newChief);
                    break;
                }
            case KnightsSubOpcode.DonationList:
                {
                    var list = new List<(string, int)>();
                    int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
                    {
                        string name = p.ReadString();
                        list.Add((name, p.RemainingBytes >= 4 ? p.ReadInt() : 0));
                    }
                    clan.RaiseClanDonationList(list);
                    break;
                }
            case KnightsSubOpcode.LeaderPoints:
                {
                    var list = new List<(string, int)>();
                    int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
                    {
                        string name = p.ReadString();
                        list.Add((name, p.RemainingBytes >= 4 ? p.ReadInt() : 0));
                    }
                    clan.RaiseClanLeaderPoints(list);
                    break;
                }
            case KnightsSubOpcode.NoticeResult:
                clan.RaiseClanNoticeRefused(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                break;
            case KnightsSubOpcode.UpdateMemo:
                if (ClanMemoReply.Read(p) is { } memo) clan.RaiseClanMemo(memo.Result, memo.Memo);
                break;
            case KnightsSubOpcode.Top10:
                {
                    if (p.RemainingBytes >= 2) p.ReadShort();
                    var top = new List<(int, int, string)>();
                    for (int i = 0; i < TopBoardEntries && p.RemainingBytes >= 2; i++)
                    {
                        int clanId = p.ReadShort();
                        string name = p.ReadString();
                        if (p.RemainingBytes >= 2) p.ReadShort();
                        int rank = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                        if (clanId > 0 && name.Length > 0)
                            top.Add((i < TopBoardPerNation ? Nations.Karus : Nations.ElMorad, rank, name));
                    }
                    clan.RaiseClanTop10(top);
                    break;
                }
        }
    }

    private void HandleKnightsList(Packet p)
    {
        var list = new List<ClanBrowseEntry>();
        if (p.RemainingBytes < 1 || p.ReadByte() != BrowsePage)
        {
            clan.RaiseClanList(list);
            return;
        }

        int count = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
        for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
        {
            var e = new ClanBrowseEntry { Id = p.ReadShort() };
            e.Name = p.ReadString();
            list.Add(e);
        }
        clan.RaiseClanList(list);
    }
}
