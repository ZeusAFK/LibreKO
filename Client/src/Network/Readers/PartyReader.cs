namespace LibreKO.Network.Readers;

internal sealed class PartyReader : IPacketReader
{
    private readonly PartySession party;
    private readonly SelfSession self;
    private readonly IPartyRequests requests;

    private const int MemberTailBytes = 2 + sizeof(int) + 1;

    public PartyReader(PartySession party, SelfSession self, IPartyRequests requests)
    {
        this.party = party;
        this.self = self;
        this.requests = requests;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.OnOtherSubs<PartySubOpcode>(GameOpcodes.GS_PARTY, HandleParty);
        dispatcher.On(GameOpcodes.GS_PARTY_BBS, HandlePartyBbs);
    }

    private void HandleParty(Packet p, PartySubOpcode sub)
    {
        switch (sub)
        {
            case PartySubOpcode.MemberInfo:
                {
                    if (p.RemainingBytes < 2) return;
                    short status = p.ReadShort();
                    if (status != PartyWire.StatusMemberRecord)
                    {
                        party.Joining = false;
                        party.RaisePartyError(status);
                        return;
                    }

                    if (p.RemainingBytes < 5) return;
                    int id = p.ReadInt();
                    byte successCode = p.ReadByte();
                    string name = p.RemainingBytes >= 2 ? p.ReadString() : "";
                    int maxHp = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    int hp = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    int level = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                    int cls = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    int maxMp = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    int mp = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    if (p.RemainingBytes >= MemberTailBytes)
                    {
                        p.ReadByte();
                        p.ReadByte();
                        int marked = p.ReadInt();
                        party.ApplyPartyType((PartyBoardType)p.ReadByte());
                        if (id == self.MyCharId && marked >= 0) party.RaiseCommanderMark(new CommanderMarkReply(marked, false));
                    }
                    var member = new PartyMember(id, successCode, name, maxHp, hp, level, cls, maxMp, mp);
                    party.Apply(member);
                    if (id != self.MyCharId) party.Rejoin.Remember(id, name);
                    party.RaisePartyMember(member);
                    break;
                }
            case PartySubOpcode.Invite:
                {
                    if (p.RemainingBytes < 4) return;
                    int inviterId = p.ReadInt();
                    string inviterName = p.RemainingBytes >= 2 ? p.ReadString() : "";
                    party.RaisePartyInvite(inviterId, inviterName);
                    break;
                }
            case PartySubOpcode.MemberLeft:
                {
                    if (p.RemainingBytes < 4) return;
                    int removedId = p.ReadInt();
                    string removedName = party.Remove(removedId);
                    if (removedId == self.MyCharId) party.Clear();
                    party.RaisePartyRemoved(removedId, removedName);
                    break;
                }
            case PartySubOpcode.Disband:
                party.Clear();
                party.RaisePartyDisband();
                break;

            case PartySubOpcode.VitalsChange:
                {
                    if (p.RemainingBytes < 12) return;
                    int cid = p.ReadInt();
                    int maxHp = p.ReadShort(); int hp = p.ReadShort();
                    int maxMp = p.ReadShort(); int mp = p.ReadShort();
                    party.Update(cid, m => new PartyMember(
                        m.CharId, m.SuccessCode, m.Name, maxHp, hp, m.Level, m.Class, maxMp, mp));
                    party.RaisePartyMemberStats(new PartyMemberVitals(CharId: cid, MaxHp: maxHp, Hp: hp, MaxMp: maxMp, Mp: mp));
                    break;
                }
            case PartySubOpcode.LevelChange:
                {
                    if (p.RemainingBytes < 5) return;
                    int cid = p.ReadInt();
                    int level = p.ReadByte();
                    party.Update(cid, m => new PartyMember(
                        m.CharId, m.SuccessCode, m.Name, m.MaxHp, m.Hp, level, m.Class, m.MaxMp, m.Mp));
                    party.RaisePartyMemberLevel(cid, level);
                    break;
                }
            case PartySubOpcode.ClassChange:
                {
                    if (p.RemainingBytes < 6) return;
                    int cid = p.ReadInt();
                    int cls = p.ReadShort();
                    party.Update(cid, m => new PartyMember(
                        m.CharId, m.SuccessCode, m.Name, m.MaxHp, m.Hp, m.Level, cls, m.MaxMp, m.Mp));
                    party.RaisePartyMemberClass(cid, cls);
                    break;
                }
            case PartySubOpcode.StatusEffect:
                {
                    if (p.RemainingBytes < 6) return;
                    int cid = p.ReadInt();
                    byte statusType = p.ReadByte();
                    bool applied = p.ReadByte() != 0;
                    party.SetStatus(cid, statusType, applied);
                    party.RaisePartyMemberStatus(cid, statusType, applied);
                    break;
                }
            case PartySubOpcode.MemberBuffs:
                if (PartyMemberBuffs.Read(p) is { } buffs)
                {
                    party.SetBuffs(buffs.CharId, buffs.SkillIds);
                    party.RaisePartyMemberBuffs(buffs.CharId, buffs.SkillIds);
                }
                break;
            case PartySubOpcode.SignalProbe:
                if (PartySignalProbe.Read(p) is { } probe) requests.SignalEcho(probe.Token);
                break;
            case PartySubOpcode.SignalTable:
                if (PartySignalTable.Read(p) is { } table)
                {
                    foreach (var entry in table) party.SetPing(entry.CharId, entry.Ms);
                    party.RaisePartySignal();
                }
                break;
            case PartySubOpcode.LevelProgress:
                if (PartyLevelProgress.Read(p) is { } progress)
                {
                    party.SetLevelPermille(progress.CharId, progress.Permille);
                    party.RaisePartyLevelProgress(progress.CharId, progress.Permille);
                }
                break;
            case PartySubOpcode.RejoinForget:
                if (p.RemainingBytes >= 4) party.Rejoin.Forget(p.ReadInt());
                break;
            case PartySubOpcode.UserVote:
                {
                    var (result, ask) = UserVoteReply.ReadStart(p);
                    if (ask is { } started) party.RaiseUserVoteAsk(started);
                    else party.RaiseUserVoteRefused(result);
                    break;
                }
            case PartySubOpcode.UserVoteResult:
                {
                    var outcome = UserVoteReply.ReadResult(p);
                    if (outcome.Counted) party.HoldVoteNotice(outcome.Warnings, outcome.Limit);
                    party.RaiseUserVoteResult(outcome);
                    break;
                }
            case PartySubOpcode.Commander:
                if (CommanderNamedReply.Read(p) is { } named)
                {
                    party.ApplyCommander(named);
                }
                break;
            case PartySubOpcode.CommanderMark:
                if (CommanderMarkReply.Read(p) is { } mark) party.RaiseCommanderMark(mark);
                break;
            case PartySubOpcode.CommanderOrder:
                if (p.RemainingBytes >= 1) party.RaiseCommanderOrder(p.ReadByte());
                break;
        }
    }

    private void HandlePartyBbs(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        party.ApplyBoardMode((PartyBoardType)p.ReadByte());
        var sub = (PartyBbsSubOpcode)p.ReadByte();
        switch (sub)
        {
            case PartyBbsSubOpcode.Register:
                party.RaisePartyBbsRegister(p.RemainingBytes >= 1 && p.ReadByte() != 0);
                break;
            case PartyBbsSubOpcode.Delete:
                party.RaisePartyBbsDelete();
                break;
            case PartyBbsSubOpcode.Wanted:
                if (p.RemainingBytes >= 1 && p.ReadByte() == 0) party.RaisePartyBbsWantedFail();
                break;
            case PartyBbsSubOpcode.List:
                ParseBbsList(p);
                break;
            case PartyBbsSubOpcode.Invite:
                if (PartyBoardInvite.Read(p) is { } invite) party.RaisePartyBbsInvite(invite);
                break;
        }
    }

    private void ParseBbsList(Packet p)
    {
        if (p.RemainingBytes < 7) return;
        p.ReadByte();
        int pageIndex = p.ReadShort();
        int count = p.ReadShort();
        int totalPages = p.ReadShort();

        var entries = new List<PartyBbsEntry>(count);
        for (int i = 0; i < count; i++)
        {
            if (p.RemainingBytes < 15) break;
            string name = p.ReadString();
            int classOrWanted = p.ReadInt();
            int level = p.ReadByte();
            p.ReadByte();
            var type = (PartyBbsEntryType)p.ReadByte();
            string message = p.ReadSByteString();
            int zoneId = p.ReadShort();
            int memberCount = p.ReadByte();
            int nation = p.ReadByte();
            p.ReadByte();
            if (type is PartyBbsEntryType.Seeker or PartyBbsEntryType.WantedParty)
                entries.Add(new PartyBbsEntry(name, classOrWanted, level, (byte)type, message, zoneId, memberCount, nation));
        }

        party.RaisePartyBbsList(pageIndex, totalPages, entries);
    }
}
