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
    public const byte Failed = 0;
    public const byte Succeeded = 1;
    public const byte HandoverLeader = 1;
    public const byte HandoverNotLeader = 2;

    public readonly record struct BrowseEntry(short ClanId, string Name);

    public readonly record struct TopEntry(short ClanId, string Name, short Rank);

    public readonly record struct Member(
        string Name, byte Fame, byte Level, short Class, bool IsOnline,
        string Memo = "", int HoursSinceLogin = 0);

    public readonly record struct Donator(string Name, int Points);

    public readonly record struct StandingEntry(short ClanId, byte Grade, byte Ranking);

    public readonly record struct AllianceOfficer(byte Fame, string Name);

    public readonly record struct AllianceClan(
        short ClanId, string Name, bool InAlliance, IReadOnlyList<AllianceOfficer> Officers);

    public readonly record struct JoinedState(
        int CharacterId, short ClanId, string ClanName, byte Fame, byte ClanType, short AllianceId,
        short MarkVersion, short CapeId, int CapeColour, byte Grade, byte Ranking);

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

    public static Packet ClanCreated(
        int characterId, short clanId, string clanName, byte grade, byte ranking, int money)
    {
        var packet = Sub(KnightsSubOpcode.Create);
        packet.WriteByte(Succeeded);
        packet.WriteInt(characterId);
        packet.WriteShort(clanId);
        packet.WriteString(clanName);
        packet.WriteByte(grade);
        packet.WriteByte(ranking);
        packet.WriteInt(money);
        return packet;
    }

    public static Packet JoinAccepted(JoinedState state)
    {
        var packet = Sub(KnightsSubOpcode.Join);
        packet.WriteByte(Succeeded);
        packet.WriteInt(state.CharacterId);
        packet.WriteShort(state.ClanId);
        packet.WriteByte(state.Fame);
        packet.WriteByte(state.ClanType);
        packet.WriteShort(state.AllianceId);
        packet.WriteShort(state.CapeId);
        packet.WriteInt(state.CapeColour);
        packet.WriteShort(state.MarkVersion);
        packet.WriteString(state.ClanName);
        packet.WriteByte(state.Grade);
        packet.WriteByte(state.Ranking);
        return packet;
    }

    public static Packet Withdrew(int characterId, short clanId, byte fame)
    {
        var packet = Sub(KnightsSubOpcode.Withdraw);
        packet.WriteByte(Succeeded);
        packet.WriteInt(characterId);
        packet.WriteShort(clanId);
        packet.WriteByte(fame);
        return packet;
    }

    public static Packet FameChanged(int characterId, short clanId, byte fame)
    {
        var packet = Sub(KnightsSubOpcode.ModifyFame);
        packet.WriteByte(Succeeded);
        packet.WriteInt(characterId);
        packet.WriteShort(clanId);
        packet.WriteByte(fame);
        return packet;
    }

    public static Packet Invitation(int inviterId, short clanId, string clanName)
    {
        var packet = Sub(KnightsSubOpcode.Invite);
        packet.WriteByte(Succeeded);
        packet.WriteInt(inviterId);
        packet.WriteShort(clanId);
        packet.WriteString(clanName);
        return packet;
    }

    public static Packet MemberOnline(string name) => MemberPresence(KnightsSubOpcode.MemberOnline, name);

    public static Packet MemberOffline(string name) => MemberPresence(KnightsSubOpcode.MemberOffline, name);

    private static Packet MemberPresence(KnightsSubOpcode sub, string name)
    {
        var packet = Sub(sub);
        packet.WriteSByteString(name);
        return packet;
    }

    public static Packet MemberList(
        short onlineCount, short maxMembers, string notice, IReadOnlyCollection<Member> members)
    {
        var packet = Sub(KnightsSubOpcode.MemberRequest);
        packet.WriteByte(MemberListPage);
        packet.WriteShort(Reserved);
        packet.WriteShort(onlineCount);
        packet.WriteShort(maxMembers);
        packet.WriteString(notice);
        packet.WriteShort((short)members.Count);

        foreach (var member in members)
        {
            packet.WriteString(member.Name);
            packet.WriteByte(member.Fame);
            packet.WriteSByteString(string.Empty);
            packet.WriteByte(member.Level);
            packet.WriteShort(member.Class);
            packet.WriteByte((byte)(member.IsOnline ? 1 : 0));
            packet.WriteString(member.Memo);
            packet.WriteInt(member.IsOnline ? 0 : member.HoursSinceLogin);
        }

        return packet;
    }

    public static Packet MemberListRefused(KnightsResult result) =>
        Result(KnightsSubOpcode.MemberRequest, (byte)result);

    public static Packet PointStatus(int nationalPoints, int pointFund)
    {
        var packet = Sub(KnightsSubOpcode.PointRequest);
        packet.WriteByte(Succeeded);
        packet.WriteInt(nationalPoints);
        packet.WriteInt(pointFund);
        return packet;
    }

    public static Packet PointStatusRefused() => Result(KnightsSubOpcode.PointRequest, Failed);

    public static Packet DonateAccepted(int nationalPoints, int pointFund, int donated)
    {
        var packet = Sub(KnightsSubOpcode.DonatePoints);
        packet.WriteByte((byte)KnightsDonateResult.Succeeded);
        packet.WriteInt(nationalPoints);
        packet.WriteInt(pointFund);
        packet.WriteByte(0);
        packet.WriteInt(donated);
        return packet;
    }

    public static Packet DonateRefused(KnightsDonateResult result) =>
        Result(KnightsSubOpcode.DonatePoints, (byte)result);

    public static Packet PointMethod(KnightsPointMethodResult result, byte method)
    {
        var packet = Sub(KnightsSubOpcode.PointMethod);
        packet.WriteByte((byte)result);
        packet.WriteByte(method);
        return packet;
    }

    public static Packet DonationList(IReadOnlyCollection<Donator> donators)
    {
        var packet = Sub(KnightsSubOpcode.DonationList);
        packet.WriteByte((byte)Math.Min(donators.Count, byte.MaxValue));
        foreach (var donator in donators.Take(byte.MaxValue))
        {
            packet.WriteString(donator.Name);
            packet.WriteInt(donator.Points);
        }
        return packet;
    }

    public static Packet LeaderPoints(IReadOnlyCollection<Donator> members)
    {
        var packet = Sub(KnightsSubOpcode.LeaderPoints);
        packet.WriteUShort((ushort)members.Count);
        foreach (var member in members)
        {
            packet.WriteString(member.Name);
            packet.WriteInt(member.Points);
        }
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

    public static Packet AllianceInvite(KnightsSubOpcode sub, string clanName, short clanId)
    {
        var packet = Sub(sub);
        packet.WriteByte(Succeeded);
        packet.WriteSByteString(clanName);
        packet.WriteShort(clanId);
        return packet;
    }

    public static Packet AllianceJoined(short mainClanId, short clanId, short capeId, int capeColour)
    {
        var packet = Sub(KnightsSubOpcode.AllyInsert);
        packet.WriteByte(Succeeded);
        packet.WriteShort(mainClanId);
        packet.WriteShort(clanId);
        packet.WriteShort(capeId);
        packet.WriteInt(capeColour);
        return packet;
    }

    public static Packet AllianceLeft(
        KnightsSubOpcode sub, short allianceId, short clanId, short capeId, int capeColour)
    {
        var packet = Sub(sub);
        packet.WriteByte(Succeeded);
        packet.WriteShort(allianceId);
        packet.WriteShort(clanId);
        packet.WriteShort(capeId);
        packet.WriteInt(capeColour);
        return packet;
    }

    public static Packet AllianceList(string notice, IReadOnlyCollection<AllianceClan> clans)
    {
        var packet = Sub(KnightsSubOpcode.AllyList);
        packet.WriteByte((byte)clans.Count);
        packet.WriteString(notice);
        foreach (var clan in clans)
        {
            packet.WriteShort(clan.ClanId);
            packet.WriteSByteString(clan.Name);
            packet.WriteByte((byte)(clan.InAlliance ? 1 : 0));
            packet.WriteByte((byte)clan.Officers.Count);
            foreach (var officer in clan.Officers)
            {
                packet.WriteByte(officer.Fame);
                packet.WriteSByteString(officer.Name);
            }
        }
        return packet;
    }

    public static Packet HandoverCandidates(byte leaderState, IReadOnlyCollection<string> names)
    {
        var packet = Sub(KnightsSubOpcode.HandoverList);
        packet.WriteByte(leaderState);
        packet.WriteUShort((ushort)names.Count);
        foreach (var name in names)
            packet.WriteString(name);
        return packet;
    }

    public static Packet HandoverDone(string oldLeader, string newLeader)
    {
        var packet = Sub(KnightsSubOpcode.HandoverReq);
        packet.WriteByte(Succeeded);
        packet.WriteString(oldLeader);
        packet.WriteString(newLeader);
        return packet;
    }

    public static Packet HandoverRefused(KnightsHandoverResult result) =>
        Result(KnightsSubOpcode.HandoverReq, (byte)result);

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
        short clanId, byte flag, short capeId, byte red, byte green, byte blue, int pointFund)
    {
        var packet = Sub(KnightsSubOpcode.Update);
        packet.WriteShort(clanId);
        packet.WriteByte(flag);
        packet.WriteShort(capeId);
        packet.WriteByte(red);
        packet.WriteByte(green);
        packet.WriteByte(blue);
        packet.WriteByte(0);
        packet.WriteInt(pointFund);
        return packet;
    }

    public static Packet StandingRefresh(IReadOnlyCollection<StandingEntry> entries)
    {
        var packet = Sub(KnightsSubOpcode.AllListRequest);
        packet.WriteShort((short)entries.Count);
        foreach (var entry in entries)
        {
            packet.WriteShort(entry.ClanId);
            packet.WriteByte(entry.Grade);
            packet.WriteByte(entry.Ranking);
        }
        return packet;
    }

    public static Packet CapeNpc() => Sub(KnightsSubOpcode.CapeNpc);

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
        }

        return packet;
    }

    public static Packet TopBoard(IReadOnlyCollection<TopEntry> entries)
    {
        var packet = Sub(KnightsSubOpcode.Top10);
        packet.WriteShort(Reserved);

        foreach (var entry in entries)
        {
            packet.WriteShort(entry.ClanId);
            packet.WriteString(entry.Name);
            packet.WriteShort(NoValue);
            packet.WriteShort(entry.Rank);
        }

        return packet;
    }

    private static Packet Sub(KnightsSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_KNIGHTS_PROCESS);
        packet.WriteByte((byte)sub);
        return packet;
    }
}
