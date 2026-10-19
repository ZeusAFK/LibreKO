using LibreKO.Common.Infrastructure.Network;

namespace LibreKO.Game.Protocol.Writers;

public sealed class PartyPacketWriter
{
    public readonly record struct WarBoardShare(int MemberId, ushort Share);

    public readonly record struct SignalEntry(int CharacterId, ushort Ms);

    public const ushort NoSignal = ushort.MaxValue;

    public const short StatusMemberRecord = 1;

    public const byte MemberJoined = 1;
    public const byte MemberPromotedToLeader = 100;

    public const int NoTargetMark = -1;
    public const int MaxMemberBuffs = byte.MaxValue;
    private const byte TrailingDefault = 0;

    public readonly record struct MemberState(
        int CharacterId,
        string Name,
        byte Level,
        short Class,
        short MaxHp,
        short Hp,
        short MaxMp,
        short Mp);

    private readonly PartySubOpcode sub;
    private readonly Action<Packet> body;

    private PartyPacketWriter(PartySubOpcode sub, Action<Packet> body)
    {
        this.sub = sub;
        this.body = body;
    }

    public static Packet MemberInfo(MemberState member, byte statusCode = MemberJoined, int markedTarget = NoTargetMark, byte partyType = TrailingDefault) =>
        new PartyPacketWriter(PartySubOpcode.MemberInfo, p =>
        {
            p.WriteShort(StatusMemberRecord);
            p.WriteInt(member.CharacterId);
            p.WriteByte(statusCode);
            p.WriteString(member.Name);
            p.WriteShort(member.MaxHp);
            p.WriteShort(member.Hp);
            p.WriteByte(member.Level);
            p.WriteShort(member.Class);
            p.WriteShort(member.MaxMp);
            p.WriteShort(member.Mp);
            p.WriteByte(TrailingDefault);
            p.WriteByte(TrailingDefault);
            p.WriteInt(markedTarget);
            p.WriteByte(partyType);
            p.WriteByte(TrailingDefault);
        }).Build();
    public static Packet Rejected(short errorCode) => new PartyPacketWriter(PartySubOpcode.MemberInfo, p => p.WriteShort(errorCode)).Build();
    public static Packet Invite(int inviterId, string inviterName) => new PartyPacketWriter(PartySubOpcode.Invite, p =>
        {
            p.WriteInt(inviterId);
            p.WriteString(inviterName);
        }).Build();
    public static Packet MemberLeft(int characterId) => new PartyPacketWriter(PartySubOpcode.MemberLeft, p => p.WriteInt(characterId)).Build();
    public static Packet RejoinForget(int characterId) => new PartyPacketWriter(PartySubOpcode.RejoinForget, p => p.WriteInt(characterId)).Build();
    public static Packet UserVoteRefused(short result) => new PartyPacketWriter(PartySubOpcode.UserVote, p => p.WriteShort(result)).Build();
    public static Packet UserVoteAsk(string targetName, byte reason, string text) => new PartyPacketWriter(PartySubOpcode.UserVote, p =>
    {
        p.WriteShort(World.PartyUserVoteRules.Started);
        p.WriteString(targetName);
        p.WriteByte(reason);
        if (reason == World.PartyUserVoteRules.ReasonOther) p.WriteString(text);
    }).Build();
    public static Packet UserVoteResult(bool warned) => new PartyPacketWriter(PartySubOpcode.UserVoteResult, p =>
    {
        p.WriteShort(warned ? UserVoteDone : UserVoteVoided);
        p.WriteByte(warned ? UserVoteWarningGiven : UserVoteVoided);
    }).Build();
    public static Packet UserVoteWarnings(short warnings, short limit) => new PartyPacketWriter(PartySubOpcode.UserVoteResult, p =>
    {
        p.WriteShort(UserVoteDone);
        p.WriteByte(UserVoteWarningCount);
        p.WriteShort(warnings);
        p.WriteShort(limit);
    }).Build();
    private const short UserVoteDone = 1;
    private const byte UserVoteVoided = 0;
    private const byte UserVoteWarningGiven = 1;
    private const byte UserVoteWarningCount = 2;
    public static Packet Disband() => new PartyPacketWriter(PartySubOpcode.Disband, static _ => { }).Build();
    public static Packet VitalsChange(
        int characterId, short maxHp, short hp, short maxMp, short mp) => new PartyPacketWriter(PartySubOpcode.VitalsChange, p =>
        {
            p.WriteInt(characterId);
            p.WriteShort(maxHp);
            p.WriteShort(hp);
            p.WriteShort(maxMp);
            p.WriteShort(mp);
        }).Build();
    public static Packet LevelChange(int characterId, byte level) => new PartyPacketWriter(PartySubOpcode.LevelChange, p =>
        {
            p.WriteInt(characterId);
            p.WriteByte(level);
        }).Build();
    public static Packet ClassChange(int characterId, short characterClass) => new PartyPacketWriter(PartySubOpcode.ClassChange, p =>
        {
            p.WriteInt(characterId);
            p.WriteShort(characterClass);
        }).Build();
    public static Packet StatusEffect(int characterId, byte statusType, bool applied) => new PartyPacketWriter(PartySubOpcode.StatusEffect, p =>
        {
            p.WriteInt(characterId);
            p.WriteByte(statusType);
            p.WriteByte(applied ? (byte)1 : (byte)0);
        }).Build();
    public static Packet MemberBuffs(int characterId, IReadOnlyList<int> skillIds) => new PartyPacketWriter(PartySubOpcode.MemberBuffs, p =>
        {
            var count = Math.Min(skillIds.Count, MaxMemberBuffs);
            p.WriteInt(characterId);
            p.WriteByte((byte)count);
            for (var index = 0; index < count; index++)
                p.WriteInt(skillIds[index]);
        }).Build();
    public static Packet SignalProbe(uint token) => new PartyPacketWriter(PartySubOpcode.SignalProbe, p => p.WriteUInt(token)).Build();
    public static Packet SignalTable(IReadOnlyList<SignalEntry> entries) => new PartyPacketWriter(PartySubOpcode.SignalTable, p =>
        {
            p.WriteByte((byte)entries.Count);
            foreach (var entry in entries)
            {
                p.WriteInt(entry.CharacterId);
                p.WriteUShort(entry.Ms);
            }
        }).Build();
    public static Packet LevelProgress(int characterId, ushort permille) => new PartyPacketWriter(PartySubOpcode.LevelProgress, p =>
        {
            p.WriteInt(characterId);
            p.WriteUShort(permille);
        }).Build();
    public static Packet Commander(int characterId, string name) => new PartyPacketWriter(PartySubOpcode.Commander, p =>
        {
            p.WriteInt(characterId);
            p.WriteString(name);
        }).Build();
    public static Packet CommanderMark(int targetId, bool clear) => new PartyPacketWriter(PartySubOpcode.CommanderMark, p =>
        {
            p.WriteInt(targetId);
            p.WriteByte((byte)(clear ? 1 : 0));
        }).Build();
    public static Packet CommanderOrder(byte order) => new PartyPacketWriter(PartySubOpcode.CommanderOrder, p => p.WriteByte(order)).Build();
    public static Packet WarBoard(byte target, byte mode, IReadOnlyList<WarBoardShare> shares) => new PartyPacketWriter(PartySubOpcode.WarBoard, p =>
        {
            p.WriteByte(target);
            p.WriteByte(mode);
            p.WriteByte((byte)shares.Count);
            foreach (var share in shares)
            {
                p.WriteInt(share.MemberId);
                p.WriteShort((short)share.Share);
            }
        }).Build();
    private Packet Build()
    {
        var packet = new Packet(GameOpcodes.GS_PARTY);
        packet.WriteByte((byte)sub);
        body(packet);
        return packet;
    }
}
