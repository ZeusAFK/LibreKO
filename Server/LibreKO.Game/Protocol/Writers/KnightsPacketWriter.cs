using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;

namespace LibreKO.Game.Protocol.Writers;

public sealed class KnightsPacketWriter
{
    public const byte MemberListPage = 1;
    public const byte BrowseListPage = 1;
    public const short Reserved = 0;
    public const short NoValue = -1;
    public const int TopBoardPerNation = 5;

    public readonly record struct BrowseEntry(
        short ClanId, string Name, string Chief, short Members, byte Flag, int Points);

    public readonly record struct TopEntry(short ClanId, string Name, short Rank);
    public const byte Failed = 0;
    public const byte Succeeded = 1;

    public readonly record struct Member(
        string Name, byte Fame, byte Level, short Class, bool IsOnline,
        string Title = "", string Note = "", int Loyalty = 0);

    public readonly record struct Donator(string Name, int Loyalty);

    public static Packet Result(KnightsSubOpcode sub, byte result)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        return packet;
    }

    public static Packet MembershipResult(KnightsSubOpcode sub, KnightsResult result) =>
        Result(sub, (byte)result);

    public static Packet CreateResult(KnightsCreateResult result) =>
        Result(KnightsSubOpcode.Create, (byte)result);

    public static Packet NoticeRefused(KnightsNoticeResult result) =>
        Result(KnightsSubOpcode.NoticeResult, (byte)result);

    public static Packet ClanInfo(
        KnightsSubOpcode sub, short clanId, string name, byte flag, short members, string chief,
        byte grade = 0, int points = 0, int pointFund = 0, string notice = "")
    {
        var packet = Sub(sub);
        packet.WriteByte(Succeeded);
        packet.WriteShort(clanId);
        packet.WriteString(name);
        packet.WriteByte(flag);
        packet.WriteShort(members);
        packet.WriteString(chief);
        packet.WriteByte(grade);
        packet.WriteInt(points);
        packet.WriteInt(pointFund);
        packet.WriteString(notice);
        return packet;
    }

    public static Packet DonateAccepted(KnightsSubOpcode sub, int loyalty)
    {
        var packet = Sub(sub);
        packet.WriteByte(Succeeded);
        packet.WriteInt(loyalty);
        return packet;
    }

    public static Packet ClanCreated(
        int characterId, short clanId, string clanName, byte grade, int money)
    {
        var packet = Sub(KnightsSubOpcode.Create);
        packet.WriteByte(Succeeded);
        packet.WriteInt(characterId);
        packet.WriteShort(clanId);
        packet.WriteString(clanName);
        packet.WriteByte(grade);
        packet.WriteByte(grade);
        packet.WriteInt(money);
        return packet;
    }

    public static Packet JoinRequestForwarded(int characterId, short clanId, string name)
    {
        var packet = Sub(KnightsSubOpcode.JoinRequestNotice);
        packet.WriteByte(Succeeded);
        packet.WriteInt(characterId);
        packet.WriteShort(clanId);
        packet.WriteString(name);
        return packet;
    }

    public readonly record struct JoinedState(
        int CharacterId, short ClanId, string ClanName, byte Fame, byte ClanType,
        short MarkVersion, short CapeId, int CapeColour, byte Grade);

    public static Packet JoinAccepted(JoinedState state)
    {
        var packet = Sub(KnightsSubOpcode.Join);
        packet.WriteByte(Succeeded);
        packet.WriteInt(state.CharacterId);
        packet.WriteShort(state.ClanId);
        packet.WriteByte(state.Fame);
        packet.WriteByte(state.ClanType);
        packet.WriteShort(Reserved);
        packet.WriteShort(state.CapeId);
        packet.WriteInt(state.CapeColour);
        packet.WriteShort(state.MarkVersion);
        packet.WriteString(state.ClanName);
        packet.WriteByte(state.Grade);
        packet.WriteByte(state.Grade);
        return packet;
    }

    public static Packet MemberList(
        KnightsSubOpcode sub, short clanId, string clanName, short totalMembers, IReadOnlyCollection<Member> members)
    {
        var packet = Sub(sub);
        packet.WriteByte(MemberListPage);
        packet.WriteShort(0);
        packet.WriteShort(totalMembers);
        packet.WriteShort(clanId);
        packet.WriteString(clanName);
        packet.WriteShort((short)members.Count);

        foreach (var member in members)
        {
            packet.WriteString(member.Name);
            packet.WriteByte(member.Fame);
            packet.WriteSByteString(member.Title);
            packet.WriteByte(member.Level);
            packet.WriteShort(member.Class);
            packet.WriteByte((byte)(member.IsOnline ? 1 : 0));
            packet.WriteString(member.Note);
            packet.WriteInt(member.Loyalty);
        }

        return packet;
    }

    public static Packet EmptyMemberList(KnightsSubOpcode sub, short clanId, string clanName)
    {
        var packet = Sub(sub);
        packet.WriteByte(MemberListPage);
        packet.WriteShort(0);
        packet.WriteShort(0);
        packet.WriteShort(clanId);
        packet.WriteString(clanName);
        packet.WriteShort(0);
        return packet;
    }

    public static Packet DonationList(KnightsSubOpcode sub, IReadOnlyCollection<Donator> donators)
    {
        var packet = Sub(sub);
        packet.WriteUShort((ushort)donators.Count);
        foreach (var donator in donators)
        {
            packet.WriteString(donator.Name);
            packet.WriteInt(donator.Loyalty);
        }
        return packet;
    }

    public static Packet NoticeUpdate(KnightsSubOpcode sub, byte result, string notice)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteString(notice);
        return packet;
    }

    public static Packet MemoUpdate(KnightsSubOpcode sub, byte kind, byte result, string memo)
    {
        var packet = Sub(sub);
        packet.WriteByte(kind);
        packet.WriteByte(result);
        packet.WriteString(memo);
        return packet;
    }

    public static Packet MemoTitle(KnightsSubOpcode sub, byte kind, byte result, string userName, string title)
    {
        var packet = Sub(sub);
        packet.WriteByte(kind);
        packet.WriteByte(result);
        packet.WriteSByteString(userName);
        packet.WriteSByteString(title);
        return packet;
    }

    public static Packet AllianceInvite(KnightsSubOpcode sub, byte result, string clanName, short clanId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteString(clanName);
        packet.WriteShort(clanId);
        return packet;
    }

    public static Packet AllianceMembership(KnightsSubOpcode sub, byte result, short mainClanId, short clanId, short slot)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteShort(mainClanId);
        packet.WriteShort(clanId);
        packet.WriteShort(slot);
        return packet;
    }

    public static Packet AllianceRemoved(KnightsSubOpcode sub, byte result, short clanId)
    {
        var packet = Sub(sub);
        packet.WriteByte(result);
        packet.WriteShort(clanId);
        return packet;
    }

    public static Packet HandoverCandidates(KnightsSubOpcode sub, byte isClanLeader, IReadOnlyCollection<string> names)
    {
        var packet = Sub(sub);
        packet.WriteByte(isClanLeader);
        packet.WriteUShort((ushort)names.Count);
        foreach (var name in names)
            packet.WriteString(name);
        return packet;
    }

    public static Packet HandoverDone(KnightsSubOpcode sub, string oldLeader, string newLeader)
    {
        var packet = Sub(sub);
        packet.WriteByte(Succeeded);
        packet.WriteString(oldLeader);
        packet.WriteString(newLeader);
        return packet;
    }

    public static Packet MarkVersion(KnightsSubOpcode sub, short failCode, ushort version)
    {
        var packet = Sub(sub);
        packet.WriteShort(failCode);
        packet.WriteUShort(version);
        return packet;
    }

    public static Packet MarkVersionFailed(KnightsSubOpcode sub, short failCode)
    {
        var packet = Sub(sub);
        packet.WriteShort(failCode);
        return packet;
    }

    public static Packet MarkRegisterResult(KnightsSubOpcode sub, ushort result, ushort version)
    {
        var packet = Sub(sub);
        packet.WriteUShort(result);
        packet.WriteUShort(version);
        return packet;
    }

    public readonly record struct AllianceMember(short ClanId, string Name, short OnlineCount);

    public static Packet AllianceList(KnightsSubOpcode sub, IReadOnlyCollection<AllianceMember> members)
    {
        var packet = Sub(sub);
        packet.WriteByte(Succeeded);
        packet.WriteByte((byte)members.Count);
        foreach (var member in members)
        {
            packet.WriteShort(member.ClanId);
            packet.WriteString(member.Name);
            packet.WriteShort(member.OnlineCount);
        }
        return packet;
    }

    public static Packet ClanMark(
        KnightsSubOpcode sub, ushort result, ushort nation, ushort clanId, ushort version, byte[] markData)
    {
        var packet = Sub(sub);
        packet.WriteUShort(result);
        packet.WriteUShort(nation);
        packet.WriteUShort(clanId);
        packet.WriteUShort(version);
        packet.WriteUShort((ushort)markData.Length);
        packet.WriteBytes(markData);
        return packet;
    }

    public static Packet ClanUpdate(
        KnightsSubOpcode sub, short clanId, byte flag, short capeId,
        byte red, byte green, byte blue, int clanPoints)
    {
        var packet = Sub(sub);
        packet.WriteShort(clanId);
        packet.WriteByte(flag);
        packet.WriteShort(capeId);
        packet.WriteByte(red);
        packet.WriteByte(green);
        packet.WriteByte(blue);
        packet.WriteByte(0);
        packet.WriteInt(clanPoints);
        return packet;
    }

    public static Packet CapeFailure(short errorCode)
    {
        var packet = new Packet(GameOpcodes.GS_CAPE);
        packet.WriteShort(errorCode);
        return packet;
    }

    public static Packet Cape(short clanId, short capeId, byte red, byte green, byte blue)
    {
        var packet = new Packet(GameOpcodes.GS_CAPE);
        packet.WriteShort((short)CapeResult.Changed);
        packet.WriteShort(clanId);
        packet.WriteShort(Reserved);
        packet.WriteShort(capeId);
        packet.WriteInt(PackColour(red, green, blue));
        return packet;
    }

    public static int PackColour(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    public static Packet CapeRefused(CapeResult result) => CapeFailure((short)result);

    public static Packet ClanBrowseList(IReadOnlyCollection<BrowseEntry> clans)
    {
        var packet = new Packet(GameOpcodes.GS_KNIGHTS_LIST);
        packet.WriteByte(BrowseListPage);
        packet.WriteShort((short)clans.Count);

        foreach (var clan in clans)
        {
            packet.WriteShort(clan.ClanId);
            packet.WriteString(clan.Name);
            packet.WriteString(clan.Chief);
            packet.WriteShort(clan.Members);
            packet.WriteByte(clan.Flag);
            packet.WriteInt(clan.Points);
        }

        return packet;
    }

    public static Packet TopBoard(KnightsSubOpcode sub, IReadOnlyCollection<TopEntry> entries)
    {
        var packet = Sub(sub);
        packet.WriteShort(0);

        foreach (var entry in entries)
        {
            packet.WriteShort(entry.ClanId);
            packet.WriteString(entry.Name);
            packet.WriteShort(NoValue);
            packet.WriteShort(entry.Rank);
        }

        return packet;
    }

    public static Packet ProcessResponse(KnightsSubOpcode sub) => Sub(sub);

    private static Packet Sub(KnightsSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
