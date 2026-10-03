using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public const byte KnCreate = 0x01, KnJoin = 0x02, KnWithdraw = 0x03, KnRemove = 0x04, KnDestroy = 0x05,
        KnAdmit = 0x06, KnReject = 0x07, KnChief = 0x09, KnVice = 0x0A, KnOfficer = 0x0B,
        KnAllList = 0x0C, KnMemberReq = 0x0D, KnModifyFame = 0x10, KnInvite = 0x11,
        KnCapeNpc = 0x1B, KnAllyCreate = 0x1C, KnAllyReq = 0x1D, KnAllyInsert = 0x1E, KnAllyRemove = 0x1F,
        KnAllyPunish = 0x20, KnAllyList = 0x22, KnUpdate = 0x24, KnMemberOnline = 0x27, KnMemberOffline = 0x28,
        KnPointReq = 0x3B, KnPointMethod = 0x3C, KnDonate = 0x3D, KnHandoverList = 0x3E, KnHandoverReq = 0x3F,
        KnDonationList = 0x40, KnNotice = 0x50, KnNoticeResult = 0x51, KnTop10 = 0x63, KnLeaderPoints = 0x64;

    public const byte KnResultOk = 1;
    public const byte KnDeclined = 11;
    public const byte KnDonateNotAccredited = 6, KnDonateNotEnough = 8;
    public const byte KnMethodNotAccredited = 6;
    private const byte KnBrowsePage = 1;
    private const int KnTopBoardEntries = 10;
    private const int KnTopBoardPerNation = 5;

    public MyClanInfo MyClan { get; private set; }

    public event Action<MyClanInfo>? MyClanChangedEvent;
    public event Action<List<ClanBrowseEntry>>? ClanListEvent;
    public event Action<List<ClanMember>>? ClanMembersEvent;
    public event Action<bool, int, string>? ClanCreateEvent;
    public event Action<int, int, string, byte, byte, byte, int, int>? EntityClanEvent;
    public event Action<int>? EntityClanClearedEvent;
    public event Action<int, int, byte>? ClanFameEvent;
    public event Action? RemovedFromClanEvent;
    public event Action<int, int, string>? ClanInviteEvent;
    public event Action<int, int>? ClanResultEvent;
    public event Action<int>? ClanNoticeRefusedEvent;
    public event Action<bool, int, int>? ClanPointStatusEvent;
    public event Action<int, int, int, int>? ClanDonateEvent;
    public event Action<int, byte>? ClanPointMethodEvent;
    public event Action<List<(string Name, int Points)>>? ClanDonationListEvent;
    public event Action<List<(string Name, int Points)>>? ClanLeaderPointsEvent;
    public event Action<List<(int Nation, int Rank, string Name)>>? ClanTop10Event;
    public event Action<bool, List<string>>? ClanHandoverListEvent;
    public event Action<bool, string, string>? ClanHandoverEvent;
    public event Action<int, int, int, int, int>? ClanCapeUpdateEvent;
    public event Action<List<ClanStanding>>? ClanStandingEvent;
    public event Action<string, bool>? ClanMemberPresenceEvent;
    public event Action? ClanCapeNpcEvent;
    public event Action<string, int>? AllianceInviteEvent;
    public event Action<int, int, bool>? AllianceMembershipEvent;
    public event Action<string, List<AllianceClanEntry>>? AllianceListEvent;

    private void SeedMyClan(MyInfo info)
    {
        MyClan = info.KnightsId > 0
            ? new MyClanInfo
            {
                InClan = true, ClanId = info.KnightsId, Name = info.ClanName ?? "", Flag = info.ClanFlag,
                Fame = info.ClanFame, Grade = info.ClanGrade, Ranking = info.ClanRanking,
                AllianceId = info.AllianceId, MarkVersion = info.MarkVersion, Notice = "",
                MaxMembers = ClanTypes.MaxMembers,
            }
            : new MyClanInfo { Notice = "", Name = "", MaxMembers = ClanTypes.MaxMembers };
        MyClanChangedEvent?.Invoke(MyClan);
    }

    internal void SeedPreviewClan(MyClanInfo clan)
    {
        MyClan = clan;
        MyClanChangedEvent?.Invoke(MyClan);
    }

    private void SetMyClan(MyClanInfo clan)
    {
        MyClan = clan;
        MyClanChangedEvent?.Invoke(MyClan);
    }

    private void ClearMyClan() => SetMyClan(new MyClanInfo { Notice = "", Name = "", MaxMembers = ClanTypes.MaxMembers });

    private void HandleKnights(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case KnCreate:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result != KnResultOk) { ClanCreateEvent?.Invoke(false, result, ""); break; }
                int charId = p.ReadInt();
                int clanId = p.ReadShort();
                string name = p.ReadString();
                byte grade = p.ReadByte(), ranking = p.ReadByte();
                int gold = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
                if (charId == MyCharId)
                {
                    SetMyClan(new MyClanInfo
                    {
                        InClan = true, ClanId = clanId, Name = name, Flag = ClanTypes.Training,
                        Fame = ClanRanks.Chief, Grade = grade, Ranking = ranking, Notice = "",
                        MaxMembers = ClanTypes.MaxMembers,
                    });
                    if (gold >= 0) GoldChangeEvent?.Invoke(gold);
                    ClanCreateEvent?.Invoke(true, 0, name);
                }
                else EntityClanEvent?.Invoke(charId, clanId, name, grade, ranking, ClanRanks.Chief, -1, 0);
                break;
            }
            case KnJoin:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result != KnResultOk) { ClanResultEvent?.Invoke(sub, result); break; }
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
                if (charId == MyCharId)
                {
                    SetMyClan(new MyClanInfo
                    {
                        InClan = true, ClanId = clanId, Name = name, Flag = flag, Fame = fame,
                        Grade = grade, Ranking = ranking, AllianceId = allianceId, MarkVersion = markVersion,
                        Notice = "", MaxMembers = ClanTypes.MaxMembers,
                    });
                    ClanResultEvent?.Invoke(sub, result);
                }
                else EntityClanEvent?.Invoke(charId, clanId, name, grade, ranking, fame, capeId, colour);
                break;
            }
            case KnWithdraw:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result != KnResultOk) { ClanResultEvent?.Invoke(sub, result); break; }
                int charId = p.RemainingBytes >= 4 ? p.ReadInt() : MyCharId;
                if (charId == MyCharId) { ClearMyClan(); ClanResultEvent?.Invoke(sub, result); }
                else EntityClanClearedEvent?.Invoke(charId);
                break;
            }
            case KnDestroy:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result == KnResultOk) ClearMyClan();
                ClanResultEvent?.Invoke(sub, result);
                break;
            }
            case KnRemove:
            case KnAdmit:
            case KnReject:
            case KnChief:
            case KnVice:
            case KnOfficer:
                ClanResultEvent?.Invoke(sub, p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                break;
            case KnAllList:
            {
                var list = new List<ClanStanding>();
                int count = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                {
                    var entry = new ClanStanding { ClanId = p.ReadShort(), Grade = p.ReadByte(), Ranking = p.ReadByte() };
                    list.Add(entry);
                    if (MyClan.InClan && entry.ClanId == MyClan.ClanId)
                    {
                        var mine = MyClan;
                        mine.Grade = entry.Grade;
                        mine.Ranking = entry.Ranking;
                        SetMyClan(mine);
                    }
                }
                ClanStandingEvent?.Invoke(list);
                break;
            }
            case KnMemberReq:
            {
                var list = new List<ClanMember>();
                byte page = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (page != KnBrowsePage) { ClanMembersEvent?.Invoke(list); break; }
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
                if (MyClan.InClan)
                {
                    var mine = MyClan;
                    mine.Notice = notice;
                    mine.Online = online;
                    mine.MaxMembers = max > 0 ? max : ClanTypes.MaxMembers;
                    SetMyClan(mine);
                }
                ClanMembersEvent?.Invoke(list);
                break;
            }
            case KnModifyFame:
            {
                if ((p.RemainingBytes >= 1 ? p.ReadByte() : 0) != KnResultOk) break;
                int charId = p.ReadInt();
                int clanId = p.ReadShort();
                byte fame = p.ReadByte();
                if (charId == MyCharId)
                {
                    if (clanId == 0)
                    {
                        bool expelled = MyClan.InClan;
                        ClearMyClan();
                        if (expelled) RemovedFromClanEvent?.Invoke();
                    }
                    else
                    {
                        var mine = MyClan;
                        mine.Fame = fame;
                        SetMyClan(mine);
                    }
                }
                ClanFameEvent?.Invoke(charId, clanId, fame);
                break;
            }
            case KnInvite:
            {
                if ((p.RemainingBytes >= 1 ? p.ReadByte() : 0) != KnResultOk) break;
                int inviterId = p.ReadInt();
                int clanId = p.ReadShort();
                ClanInviteEvent?.Invoke(inviterId, clanId, p.ReadString());
                break;
            }
            case KnCapeNpc:
                ClanCapeNpcEvent?.Invoke();
                break;
            case KnAllyCreate:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result == KnResultOk && MyClan.InClan)
                {
                    var mine = MyClan;
                    mine.AllianceId = mine.ClanId;
                    SetMyClan(mine);
                    AllianceMembershipEvent?.Invoke(mine.ClanId, mine.ClanId, true);
                }
                else ClanResultEvent?.Invoke(sub, result);
                break;
            }
            case KnAllyReq:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result != KnResultOk) { ClanResultEvent?.Invoke(sub, result); break; }
                string name = p.ReadSByteString();
                int clanId = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                AllianceInviteEvent?.Invoke(name, clanId);
                break;
            }
            case KnAllyInsert:
            {
                if ((p.RemainingBytes >= 1 ? p.ReadByte() : 0) != KnResultOk) { ClanResultEvent?.Invoke(sub, 0); break; }
                int mainId = p.ReadShort();
                int clanId = p.ReadShort();
                int capeId = p.ReadShort();
                int colour = p.ReadInt();
                if (MyClan.InClan && clanId == MyClan.ClanId)
                {
                    var mine = MyClan;
                    mine.AllianceId = mainId;
                    SetMyClan(mine);
                }
                ClanCapeUpdateEvent?.Invoke(clanId, capeId, colour & 0xFF, (colour >> 8) & 0xFF, (colour >> 16) & 0xFF);
                AllianceMembershipEvent?.Invoke(mainId, clanId, true);
                break;
            }
            case KnAllyRemove:
            case KnAllyPunish:
            {
                if ((p.RemainingBytes >= 1 ? p.ReadByte() : 0) != KnResultOk) { ClanResultEvent?.Invoke(sub, 0); break; }
                int allianceId = p.ReadShort();
                int clanId = p.ReadShort();
                int capeId = p.ReadShort();
                int colour = p.ReadInt();
                if (MyClan.InClan && (clanId == MyClan.ClanId || allianceId == MyClan.ClanId && clanId == allianceId))
                {
                    var mine = MyClan;
                    mine.AllianceId = 0;
                    SetMyClan(mine);
                }
                if (capeId >= 0)
                    ClanCapeUpdateEvent?.Invoke(clanId, capeId, colour & 0xFF, (colour >> 8) & 0xFF, (colour >> 16) & 0xFF);
                AllianceMembershipEvent?.Invoke(allianceId, clanId, false);
                break;
            }
            case KnAllyList:
            {
                var clans = new List<AllianceClanEntry>();
                int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                if (count == 0) { AllianceListEvent?.Invoke("", clans); break; }
                string notice = p.ReadString();
                for (int i = 0; i < count && p.RemainingBytes >= 4; i++)
                {
                    var clan = new AllianceClanEntry { Id = p.ReadShort(), Name = p.ReadSByteString(), Officers = new List<AllianceOfficer>() };
                    clan.InAlliance = p.ReadByte() != 0;
                    int officers = p.ReadByte();
                    for (int j = 0; j < officers && p.RemainingBytes >= 2; j++)
                        clan.Officers.Add(new AllianceOfficer { Fame = p.ReadByte(), Name = p.ReadSByteString() });
                    clans.Add(clan);
                }
                AllianceListEvent?.Invoke(notice, clans);
                break;
            }
            case KnUpdate:
            {
                if (p.RemainingBytes < 8) break;
                int clanId = p.ReadShort();
                byte flag = p.ReadByte();
                int capeId = p.ReadShort();
                int r = p.ReadByte(), g = p.ReadByte(), b = p.ReadByte();
                p.ReadByte();
                int fund = p.RemainingBytes >= 4 ? p.ReadInt() : -1;
                if (MyClan.InClan && clanId == MyClan.ClanId)
                {
                    var mine = MyClan;
                    mine.Flag = flag;
                    if (fund >= 0) mine.PointFund = fund;
                    SetMyClan(mine);
                    var me = LastEnter;
                    me.CapeId = capeId;
                    me.CapeR = r;
                    me.CapeG = g;
                    me.CapeB = b;
                    LastEnter = me;
                }
                ClanCapeUpdateEvent?.Invoke(clanId, capeId, r, g, b);
                break;
            }
            case KnMemberOnline:
            case KnMemberOffline:
                ClanMemberPresenceEvent?.Invoke(p.ReadSByteString(), sub == KnMemberOnline);
                break;
            case KnPointReq:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == KnResultOk;
                int np = ok && p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                int fund = ok && p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                if (ok && MyClan.InClan)
                {
                    var mine = MyClan;
                    mine.PointFund = fund;
                    SetMyClan(mine);
                }
                ClanPointStatusEvent?.Invoke(ok, np, fund);
                break;
            }
            case KnPointMethod:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                byte method = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result == KnResultOk && MyClan.InClan)
                {
                    var mine = MyClan;
                    mine.PointMethod = method;
                    SetMyClan(mine);
                }
                ClanPointMethodEvent?.Invoke(result, method);
                break;
            }
            case KnDonate:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result != KnResultOk) { ClanDonateEvent?.Invoke(result, 0, 0, 0); break; }
                int np = p.ReadInt();
                int fund = p.ReadInt();
                p.ReadByte();
                int amount = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                if (MyClan.InClan)
                {
                    var mine = MyClan;
                    mine.PointFund = fund;
                    SetMyClan(mine);
                }
                ClanDonateEvent?.Invoke(result, np, fund, amount);
                break;
            }
            case KnHandoverList:
            {
                byte state = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                var names = new List<string>();
                if (state == KnResultOk)
                {
                    int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    for (int i = 0; i < count && p.RemainingBytes >= 2; i++) names.Add(p.ReadString());
                }
                ClanHandoverListEvent?.Invoke(state == KnResultOk, names);
                break;
            }
            case KnHandoverReq:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result != KnResultOk) { ClanHandoverEvent?.Invoke(false, "", ""); break; }
                string oldChief = p.ReadString();
                string newChief = p.ReadString();
                if (MyClan.InClan)
                {
                    var mine = MyClan;
                    if (string.Equals(newChief, LastEnter.Name, StringComparison.OrdinalIgnoreCase)) mine.Fame = ClanRanks.Chief;
                    else if (string.Equals(oldChief, LastEnter.Name, StringComparison.OrdinalIgnoreCase)) mine.Fame = ClanRanks.Trainee;
                    SetMyClan(mine);
                }
                ClanHandoverEvent?.Invoke(true, oldChief, newChief);
                break;
            }
            case KnDonationList:
            {
                var list = new List<(string, int)>();
                int count = p.RemainingBytes >= 1 ? p.ReadByte() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
                {
                    string name = p.ReadString();
                    list.Add((name, p.RemainingBytes >= 4 ? p.ReadInt() : 0));
                }
                ClanDonationListEvent?.Invoke(list);
                break;
            }
            case KnLeaderPoints:
            {
                var list = new List<(string, int)>();
                int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
                {
                    string name = p.ReadString();
                    list.Add((name, p.RemainingBytes >= 4 ? p.ReadInt() : 0));
                }
                ClanLeaderPointsEvent?.Invoke(list);
                break;
            }
            case KnNoticeResult:
                ClanNoticeRefusedEvent?.Invoke(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                break;
            case KnTop10:
            {
                if (p.RemainingBytes >= 2) p.ReadShort();
                var top = new List<(int, int, string)>();
                for (int i = 0; i < KnTopBoardEntries && p.RemainingBytes >= 2; i++)
                {
                    int clanId = p.ReadShort();
                    string name = p.ReadString();
                    if (p.RemainingBytes >= 2) p.ReadShort();
                    int rank = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    if (clanId > 0 && name.Length > 0)
                        top.Add((i < KnTopBoardPerNation ? Nations.Karus : Nations.ElMorad, rank, name));
                }
                ClanTop10Event?.Invoke(top);
                break;
            }
        }
    }

    private void HandleKnightsList(Packet p)
    {
        var list = new List<ClanBrowseEntry>();
        if (p.RemainingBytes < 1 || p.ReadByte() != KnBrowsePage)
        {
            ClanListEvent?.Invoke(list);
            return;
        }

        int count = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
        for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
        {
            var e = new ClanBrowseEntry { Id = p.ReadShort() };
            e.Name = p.ReadString();
            list.Add(e);
        }
        ClanListEvent?.Invoke(list);
    }

    public void SendClanList(int page)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(KnAllList);
        p.WriteUShort((ushort)page);
        _conn.Send(p);
    }

    public void SendClanTop10() => SendKnightsByte(KnTop10);
    public void SendClanDonationList() => SendKnightsByte(KnDonationList);
    public void SendClanLeaderPoints() => SendKnightsByte(KnLeaderPoints);
    public void SendClanPointStatus() => SendKnightsByte(KnPointReq);
    public void SendClanMembersRequest() => SendKnightsByte(KnMemberReq);
    public void SendClanWithdraw() => SendKnightsByte(KnWithdraw);
    public void SendClanDestroy() => SendKnightsByte(KnDestroy);
    public void SendClanHandoverList() => SendKnightsByte(KnHandoverList);
    public void SendAllianceList() => SendKnightsByte(KnAllyList);
    public void SendAllianceLeave() => SendKnightsByte(KnAllyRemove);

    public void SendClanCreate(string name) => SendKnightsString(KnCreate, name);
    public void SendClanKick(string name) => SendKnightsString(KnRemove, name);
    public void SendClanAdmit(string name) => SendKnightsString(KnAdmit, name);
    public void SendClanReject(string name) => SendKnightsString(KnReject, name);
    public void SendClanPromoteVice(string name) => SendKnightsString(KnVice, name);
    public void SendClanPromoteOfficer(string name) => SendKnightsString(KnOfficer, name);
    public void SendClanHandover(string name) => SendKnightsString(KnHandoverReq, name);
    public void SendClanNotice(string notice) => SendKnightsString(KnNotice, notice);

    public void SendClanInvite(int charId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(KnJoin);
        p.WriteInt(charId);
        _conn.Send(p);
    }

    public void SendClanInviteAnswer(bool accept, int inviterId, int clanId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(KnInvite);
        p.WriteByte((byte)(accept ? 1 : 0));
        p.WriteInt(inviterId);
        p.WriteShort((short)clanId);
        _conn.Send(p);
    }

    public void SendClanDonate(int amount)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(KnDonate);
        p.WriteInt(amount);
        _conn.Send(p);
    }

    public void SendClanPointMethod(byte method)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(KnPointMethod);
        p.WriteByte((byte)(method + 1));
        _conn.Send(p);
    }

    public void SendAllianceCreate(int chiefCharId) => SendKnightsInt(KnAllyCreate, chiefCharId);
    public void SendAllianceInsert(int chiefCharId) => SendKnightsInt(KnAllyInsert, chiefCharId);

    public void SendAllianceAnswer(bool accept)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(KnAllyReq);
        p.WriteByte((byte)(accept ? 1 : 0));
        _conn.Send(p);
    }

    public void SendAlliancePunish(int clanId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(KnAllyPunish);
        p.WriteShort((short)clanId);
        _conn.Send(p);
    }

    private void SendKnightsByte(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(sub);
        _conn.Send(p);
    }

    private void SendKnightsInt(byte sub, int value)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(sub);
        p.WriteInt(value);
        _conn.Send(p);
    }

    private void SendKnightsString(byte sub, string s)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(sub);
        p.WriteString(s);
        _conn.Send(p);
    }
}
