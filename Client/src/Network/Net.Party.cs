using System;
using System.Collections.Generic;

namespace LibreKO.Network;

public partial class Net
{
    public event Action<PartyMember>? PartyMemberEvent;

    public event Action<int>? PartyErrorEvent;

    public event Action<int, string>? PartyInviteEvent;

    public event Action<int, string>? PartyRemovedEvent;

    public event Action<int, int, int, int, int>? PartyMemberStatsEvent;

    public event Action<int, int>? PartyMemberLevelEvent;

    public event Action<int, int>? PartyMemberClassEvent;

    public event Action<int, byte, bool>? PartyMemberStatusEvent;

    public event Action? PartyDisbandEvent;

    public event Action<bool>? PartyBbsRegisterEvent;

    public event Action? PartyBbsDeleteEvent;

    public event Action<int, int, List<PartyBbsEntry>>? PartyBbsListEvent;

    public event Action? PartyBbsWantedFailEvent;

    private readonly List<PartyMember> _party = new();
    private readonly Dictionary<int, HashSet<byte>> _partyStatus = new();

    public IReadOnlyList<PartyMember> Party => _party;

    public bool InParty => _party.Count > 0;

    public IReadOnlyCollection<byte>? PartyStatusOf(int charId) =>
        _partyStatus.TryGetValue(charId, out var status) ? status : null;

    public void SeedPartyPreview(params PartyMember[] members)
    {
        _party.Clear();
        _party.AddRange(members);
    }

    private void ClearParty(string reason)
    {
        if (_party.Count > 0) Godot.GD.Print($"[party] roster cleared: {reason}");
        _party.Clear();
        _partyStatus.Clear();
    }

    private PartyMember SelfPartyMember() =>
        new(MyCharId, 1, LastEnter.Name is { Length: > 0 } name ? name : "You",
            Vitals.MaxHp, Vitals.Hp, Sheet.Level, LastEnter.Class, Vitals.MaxMp, Vitals.Mp);

    private void ApplyPartyMember(PartyMember m)
    {
        if (m.BecameLeader)
        {
            _party.RemoveAll(x => x.CharId == m.CharId);
            _party.Insert(0, m);
        }
        else
        {
            int idx = _party.FindIndex(x => x.CharId == m.CharId);
            if (idx >= 0) _party[idx] = m;
            else _party.Add(m);
        }

        if (_party.FindIndex(x => x.CharId == MyCharId) < 0)
            _party.Insert(0, SelfPartyMember());
    }

    private void UpdatePartyMember(int charId, Func<PartyMember, PartyMember> update)
    {
        int idx = _party.FindIndex(x => x.CharId == charId);
        if (idx >= 0) _party[idx] = update(_party[idx]);
    }

    public static class PartyRequest
    {
        public const byte Create = 1, Permit = 2, Insert = 3, Remove = 4, Delete = 5, Promote = 28;
    }

    public const byte SubPartyInvite = 2, SubPartyMemberInfo = 3, SubPartyMemberLeft = 4,
                      SubPartyDisband = 5, SubPartyVitals = 6, SubPartyLevel = 7,
                      SubPartyClass = 8, SubPartyStatusEffect = 9, SubPartyCommander = 30,
                      SubPartyTargetNumber = 31, SubPartyAlert = 32;

    public const short PartyStatusMemberRecord = 1;

    private void HandleParty(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        byte sub = p.ReadByte();
        switch (sub)
        {
            case SubPartyMemberInfo:
            {
                if (p.RemainingBytes < 2) return;
                short status = p.ReadShort();
                if (status != PartyStatusMemberRecord) { PartyErrorEvent?.Invoke(status); return; }

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
                var member = new PartyMember(id, successCode, name, maxHp, hp, level, cls, maxMp, mp);
                ApplyPartyMember(member);
                PartyMemberEvent?.Invoke(member);
                break;
            }
            case SubPartyInvite:
            {
                if (p.RemainingBytes < 4) return;
                int inviterId = p.ReadInt();
                string inviterName = p.RemainingBytes >= 2 ? p.ReadString() : "";
                PartyInviteEvent?.Invoke(inviterId, inviterName);
                break;
            }
            case SubPartyMemberLeft:
            {
                if (p.RemainingBytes < 4) return;
                int removedId = p.ReadInt();
                int idx = _party.FindIndex(x => x.CharId == removedId);
                string removedName = idx >= 0 ? _party[idx].Name : "";
                if (idx >= 0) _party.RemoveAt(idx);
                _partyStatus.Remove(removedId);
                if (removedId == MyCharId) ClearParty("the server removed us from the party (GS_PARTY sub 4)");
                PartyRemovedEvent?.Invoke(removedId, removedName);
                break;
            }
            case SubPartyDisband:
                ClearParty("the server disbanded the party (GS_PARTY sub 5)");
                PartyDisbandEvent?.Invoke();
                break;

            case SubPartyVitals:
            {
                if (p.RemainingBytes < 12) return;
                int cid = p.ReadInt();
                int maxHp = p.ReadShort(); int hp = p.ReadShort();
                int maxMp = p.ReadShort(); int mp = p.ReadShort();
                UpdatePartyMember(cid, m => new PartyMember(
                    m.CharId, m.SuccessCode, m.Name, maxHp, hp, m.Level, m.Class, maxMp, mp));
                PartyMemberStatsEvent?.Invoke(cid, maxHp, hp, maxMp, mp);
                break;
            }
            case SubPartyLevel:
            {
                if (p.RemainingBytes < 5) return;
                int cid = p.ReadInt();
                int level = p.ReadByte();
                UpdatePartyMember(cid, m => new PartyMember(
                    m.CharId, m.SuccessCode, m.Name, m.MaxHp, m.Hp, level, m.Class, m.MaxMp, m.Mp));
                PartyMemberLevelEvent?.Invoke(cid, level);
                break;
            }
            case SubPartyClass:
            {
                if (p.RemainingBytes < 6) return;
                int cid = p.ReadInt();
                int cls = p.ReadShort();
                UpdatePartyMember(cid, m => new PartyMember(
                    m.CharId, m.SuccessCode, m.Name, m.MaxHp, m.Hp, m.Level, cls, m.MaxMp, m.Mp));
                PartyMemberClassEvent?.Invoke(cid, cls);
                break;
            }
            case SubPartyStatusEffect:
            {
                if (p.RemainingBytes < 6) return;
                int cid = p.ReadInt();
                byte statusType = p.ReadByte();
                bool applied = p.ReadByte() != 0;
                if (statusType != 0)
                {
                    if (!_partyStatus.TryGetValue(cid, out var set))
                    {
                        set = new HashSet<byte>();
                        _partyStatus[cid] = set;
                    }
                    if (applied) set.Add(statusType); else set.Remove(statusType);
                }
                PartyMemberStatusEvent?.Invoke(cid, statusType, applied);
                break;
            }

        }
    }

    private void HandlePartyBbs(Packet p)
    {
        if (p.RemainingBytes < 2) return;
        p.ReadByte();
        byte sub = p.ReadByte();
        switch (sub)
        {
            case 0x01:
                PartyBbsRegisterEvent?.Invoke(p.RemainingBytes >= 1 && p.ReadByte() != 0);
                break;
            case 0x02:
                PartyBbsDeleteEvent?.Invoke();
                break;
            case 0x04:
                if (p.RemainingBytes >= 1 && p.ReadByte() == 0) PartyBbsWantedFailEvent?.Invoke();
                break;
            case 0x0B:
                ParseBbsList(p);
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
            byte type = p.ReadByte();
            p.ReadByte();
            string message = p.ReadSByteString();
            int zoneId = p.ReadShort();
            int memberCount = p.ReadByte();
            int nation = p.ReadByte();
            p.ReadByte();
            if (type == 2 || type == 3)
                entries.Add(new PartyBbsEntry(name, classOrWanted, level, type, message, zoneId, memberCount, nation));
        }

        PartyBbsListEvent?.Invoke(pageIndex, totalPages, entries);
    }
}
