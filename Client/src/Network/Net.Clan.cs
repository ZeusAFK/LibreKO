using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    private const byte KnCreate = 0x01, KnJoin = 0x02, KnWithdraw = 0x03, KnRemove = 0x04, KnDestroy = 0x05,
        KnAdmit = 0x06, KnReject = 0x07, KnChief = 0x09, KnVice = 0x0A, KnOfficer = 0x0B,
        KnMemberReq = 0x0D, KnCurrentReq = 0x0E, KnJoinReqNotice = 0x0F, KnCapeNpc = 0x14,
        KnDonate = 0x3D, KnUpdate = 0x24,
        KnNotice = 0x50, KnNoticeResult = 0x51, KnTop10 = 0x63, KnDonationList = 0x40;

    private const byte KnBrowsePage = 1;

    public event Action<List<ClanBrowseEntry>>? ClanListEvent;
    public event Action<MyClanInfo>? MyClanInfoEvent;
    public event Action<List<ClanMember>>? ClanMembersEvent;
    public event Action<bool, int, string>? ClanCreateEvent;
    public event Action<int, string>? ClanJoinedEvent;
    public event Action<int, string>? ClanJoinRequestEvent;
    public event Action<int, int>? ClanResultEvent;
    public event Action<string>? ClanNoticeEvent;
    public event Action<int>? ClanNoticeRefusedEvent;
    public event Action<bool, int>? ClanDonateEvent;
    public event Action<List<(int Nation, int Rank, string Name)>>? ClanTop10Event;
    public event Action<List<(string Name, int Loyalty)>>? ClanDonationListEvent;
    public event Action<int, int, int, int, int>? ClanCapeUpdateEvent;

    public event Action? ClanCapeNpcEvent;

    private void HandleKnights(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case KnCreate:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result == 1)
                {
                    if (p.RemainingBytes >= 4) p.ReadInt();
                    if (p.RemainingBytes >= 2) p.ReadShort();
                    string name = p.ReadString();
                    ClanCreateEvent?.Invoke(true, 0, name);
                }
                else ClanCreateEvent?.Invoke(false, result, "");
                break;
            }
            case KnJoin:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result != 1)
                {
                    ClanResultEvent?.Invoke(KnJoin, result);
                    break;
                }

                if (p.RemainingBytes >= 4) p.ReadInt();
                int clanId = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                if (p.RemainingBytes >= 1) p.ReadByte();
                if (p.RemainingBytes >= 1) p.ReadByte();
                if (p.RemainingBytes >= 2) p.ReadShort();
                int capeId = p.RemainingBytes >= 2 ? p.ReadShort() : -1;
                int colour = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                if (p.RemainingBytes >= 2) p.ReadShort();
                string name = p.ReadString();
                ClanJoinedEvent?.Invoke(clanId, name);
                ClanCapeUpdateEvent?.Invoke(
                    clanId, capeId, colour & 0xFF, (colour >> 8) & 0xFF, (colour >> 16) & 0xFF);
                break;
            }
            case KnCapeNpc:
                ClanCapeNpcEvent?.Invoke();
                break;
            case KnWithdraw:
            case KnRemove:
            case KnDestroy:
            case KnChief:
            case KnVice:
            case KnOfficer:
                ClanResultEvent?.Invoke(sub, p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                break;
            case KnMemberReq:
            {
                var list = new List<ClanMember>();
                if (p.RemainingBytes < 9) { ClanMembersEvent?.Invoke(list); break; }
                p.ReadByte();
                p.ReadShort();
                p.ReadShort();
                p.ReadShort();
                p.ReadString();
                int count = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 3; i++)
                {
                    var m = new ClanMember { Name = p.ReadString() };
                    m.Fame = (byte)p.ReadByte();
                    m.Title = p.RemainingBytes >= 1 ? p.ReadSByteString() : string.Empty;
                    m.Level = p.RemainingBytes >= 1 ? (byte)p.ReadByte() : (byte)0;
                    m.Class = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    m.IsOnline = p.RemainingBytes >= 1 && p.ReadByte() != 0;
                    m.Note = p.RemainingBytes >= 2 ? p.ReadString() : string.Empty;
                    m.Loyalty = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    list.Add(m);
                }
                ClanMembersEvent?.Invoke(list);
                break;
            }
            case KnCurrentReq:
            {
                byte ok = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (ok == 1)
                {
                    var info = new MyClanInfo { InClan = true };
                    info.ClanId = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                    info.Name = p.ReadString();
                    info.Flag = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                    info.Members = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    info.Chief = p.ReadString();
                    info.Grade = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
                    info.Points = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    info.PointFund = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    info.Notice = p.RemainingBytes >= 2 ? p.ReadString() : "";
                    MyClanInfoEvent?.Invoke(info);
                }
                else MyClanInfoEvent?.Invoke(new MyClanInfo { InClan = false });
                break;
            }
            case KnJoinReqNotice:
            {
                byte result = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                if (result == 1)
                {
                    int charId = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    if (p.RemainingBytes >= 2) p.ReadShort();
                    string name = p.ReadString();
                    ClanJoinRequestEvent?.Invoke(charId, name);
                }
                break;
            }
            case KnNotice:
            {
                if (p.RemainingBytes >= 1) p.ReadByte();
                ClanNoticeEvent?.Invoke(p.RemainingBytes >= 2 ? p.ReadString() : "");
                break;
            }
            case KnNoticeResult:
            {
                byte code = p.RemainingBytes >= 1 ? p.ReadByte() : (byte)0;
                ClanNoticeRefusedEvent?.Invoke(code);
                break;
            }
            case KnDonate:
            {
                bool ok = (p.RemainingBytes >= 1 ? p.ReadByte() : 0) == 1;
                int loyalty = ok && p.RemainingBytes >= 4 ? p.ReadInt() : -1;
                if (ok && loyalty >= 0) LoyaltyChangeEvent?.Invoke(loyalty, 0);
                ClanDonateEvent?.Invoke(ok, loyalty);
                break;
            }
            case KnTop10:
            {
                if (p.RemainingBytes >= 2) p.ReadShort();
                var top = new List<(int, int, string)>();
                for (int i = 0; i < 10 && p.RemainingBytes >= 2; i++)
                {
                    int clanId = p.ReadShort();
                    string name = p.ReadString();
                    if (p.RemainingBytes >= 2) p.ReadShort();
                    int rank = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
                    if (clanId > 0 && name.Length > 0) top.Add((i < 5 ? 1 : 2, rank, name));
                }
                ClanTop10Event?.Invoke(top);
                break;
            }
            case KnDonationList:
            {
                var list = new List<(string, int)>();
                int count = p.RemainingBytes >= 2 ? p.ReadUShort() : 0;
                for (int i = 0; i < count && p.RemainingBytes >= 2; i++)
                {
                    string name = p.ReadString();
                    int loyalty = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
                    list.Add((name, loyalty));
                }
                ClanDonationListEvent?.Invoke(list);
                break;
            }
            case KnUpdate:
            {
                if (p.RemainingBytes < 8) break;
                int clanId = p.ReadShort();
                p.ReadByte();
                int capeId = p.ReadShort();
                int r = p.ReadByte(), g = p.ReadByte(), b = p.ReadByte();
                ClanCapeUpdateEvent?.Invoke(clanId, capeId, r, g, b);
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
            e.Chief = p.ReadString();
            e.Members = p.RemainingBytes >= 2 ? p.ReadShort() : 0;
            e.Flag = (byte)(p.RemainingBytes >= 1 ? p.ReadByte() : 0);
            e.Points = p.RemainingBytes >= 4 ? p.ReadInt() : 0;
            list.Add(e);
        }
        ClanListEvent?.Invoke(list);
    }

    public void SendClanList() { var p = new Packet(GameOpcodes.GS_KNIGHTS_LIST); _conn.Send(p); }
    public void SendClanTop10() => SendKnightsByte(KnTop10);
    public void SendClanDonationList() => SendKnightsByte(KnDonationList);
    public void SendClanInfoRequest() => SendKnightsByte(KnCurrentReq);
    public void SendClanMembersRequest() => SendKnightsByte(KnMemberReq);
    public void SendClanWithdraw() => SendKnightsByte(KnWithdraw);
    public void SendClanDestroy() => SendKnightsByte(KnDestroy);

    public void SendClanCreate(string name) => SendKnightsString(KnCreate, name);
    public void SendClanKick(string name) => SendKnightsString(KnRemove, name);
    public void SendClanAdmit(string name) => SendKnightsString(KnAdmit, name);
    public void SendClanReject(string name) => SendKnightsString(KnReject, name);
    public void SendClanPromoteChief(string name) => SendKnightsString(KnChief, name);
    public void SendClanPromoteVice(string name) => SendKnightsString(KnVice, name);
    public void SendClanPromoteOfficer(string name) => SendKnightsString(KnOfficer, name);
    public void SendClanNotice(string notice) => SendKnightsString(KnNotice, notice);

    public void SendClanJoinRequest(int clanId)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(KnJoin);
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

    private void SendKnightsByte(byte sub)
    {
        var p = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        p.WriteByte(sub);
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
