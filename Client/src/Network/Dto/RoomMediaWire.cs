namespace LibreKO.Network;

public static class RoomMediaWire
{
    public const int PeersMax = 200;
    public const int HistoryRowsMax = 50;
}

public readonly record struct VoicePeer(int CharacterId, byte Flags);

public sealed record VoiceSession(ushort UdpPort, uint SessionId, byte[] Key, byte SelfFlags, IReadOnlyList<VoicePeer> Peers);

public static class RoomMediaReplies
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static RoomPost? ReadRow(Packet p)
    {
        if (p.RemainingBytes < 15) return null;
        long id = p.ReadLong();
        var kind = (RoomPostKind)p.ReadByte();
        int sender = p.ReadInt();
        string name = p.ReadString();
        if (p.RemainingBytes < 4) return null;
        var time = Epoch.AddSeconds(p.ReadUInt()).ToLocalTime();
        if (kind == RoomPostKind.Text)
        {
            if (p.RemainingBytes < 2) return null;
            return new RoomPost(id, kind, sender, name, time, p.ReadString(), 0, 0);
        }
        if (kind != RoomPostKind.VoiceNote || p.RemainingBytes < 6) return null;
        int duration = p.ReadUShort();
        int size = (int)p.ReadUInt();
        byte[] waveform = [];
        if (p.RemainingBytes >= 1)
        {
            int samples = p.ReadByte();
            if (samples > p.RemainingBytes) return null;
            waveform = p.ReadBytes(samples);
        }
        return new RoomPost(id, kind, sender, name, time, "", duration, size) { Waveform = waveform };
    }

    public readonly record struct ReactionChange(long MessageId, int CharacterId, string Name, string Emoji);

    public static ReactionChange? ReadReaction(Packet p)
    {
        if (p.RemainingBytes < 16) return null;
        long id = p.ReadLong();
        int who = p.ReadInt();
        string name = p.ReadString();
        if (p.RemainingBytes < 2) return null;
        return new ReactionChange(id, who, name, p.ReadString());
    }

    public static List<ReactionChange> ReadReactions(Packet p)
    {
        var list = new List<ReactionChange>();
        if (p.RemainingBytes < 2) return list;
        int count = p.ReadUShort();
        for (int i = 0; i < count; i++)
        {
            if (ReadReaction(p) is not { } reaction) break;
            list.Add(reaction);
        }
        return list;
    }

    public static (string Partner, RoomPost Row)? ReadDirectRow(Packet p)
    {
        if (p.RemainingBytes < 2) return null;
        string partner = p.ReadString();
        return partner.Length > 0 && ReadRow(p) is { } row ? (partner, row) : null;
    }

    public static (string Partner, List<RoomPost> Rows, bool More) ReadDirectHistory(Packet p)
    {
        string partner = p.RemainingBytes >= 2 ? p.ReadString() : "";
        var (rows, more) = ReadHistory(p);
        return (partner, rows, more);
    }

    public static (byte Result, bool Kept)? ReadKept(Packet p)
    {
        if (p.RemainingBytes < 2) return null;
        return (p.ReadByte(), p.ReadByte() != 0);
    }

    public static (List<RoomPost> Rows, bool More) ReadHistory(Packet p)
    {
        var rows = new List<RoomPost>();
        if (p.RemainingBytes < 1) return (rows, false);
        int count = Math.Min((int)p.ReadByte(), RoomMediaWire.HistoryRowsMax);
        for (int i = 0; i < count; i++)
        {
            if (ReadRow(p) is not { } row) return (rows, false);
            rows.Add(row);
        }
        bool more = p.RemainingBytes >= 1 && p.ReadByte() != 0;
        return (rows, more);
    }

    public readonly record struct NoteHeader(RoomMediaResult Result, long NoteId, int Size);

    public readonly record struct NoteChunk(long NoteId, ushort Index, byte[] Data);

    public readonly record struct VoiceChange(int CharacterId, RoomMemberChange Change, byte Flags);

    public readonly record struct VoiceFrame(int Speaker, ushort Seq, byte[] Opus);

    public readonly record struct SilenceNotice(int TargetId, bool Silenced, string Leader, string Target)
    {
        public string Text => Silenced ? $"{Leader} silenced {Target}'s microphone." : $"{Leader} let {Target} speak again.";
    }

    public static NoteHeader? ReadNoteHeader(Packet p)
    {
        if (p.RemainingBytes < 9) return null;
        var result = (RoomMediaResult)p.ReadByte();
        long id = p.ReadLong();
        int size = result == RoomMediaResult.Ok && p.RemainingBytes >= 4 ? (int)p.ReadUInt() : 0;
        return new NoteHeader(result, id, size);
    }

    public static NoteChunk? ReadNoteChunk(Packet p)
    {
        if (p.RemainingBytes < 12) return null;
        long id = p.ReadLong();
        ushort index = p.ReadUShort();
        int length = p.ReadUShort();
        if (length > p.RemainingBytes) return null;
        return new NoteChunk(id, index, p.ReadBytes(length));
    }

    public static VoiceChange? ReadVoiceChange(Packet p)
    {
        if (p.RemainingBytes < 6) return null;
        return new VoiceChange(p.ReadInt(), (RoomMemberChange)p.ReadByte(), p.ReadByte());
    }

    public static VoiceFrame? ReadVoiceFrame(Packet p)
    {
        if (p.RemainingBytes < 8) return null;
        int speaker = p.ReadInt();
        ushort seq = p.ReadUShort();
        int length = p.ReadUShort();
        if (length == 0 || length > p.RemainingBytes) return null;
        return new VoiceFrame(speaker, seq, p.ReadBytes(length));
    }

    public static SilenceNotice? ReadSilenceNotice(Packet p)
    {
        if (p.RemainingBytes < 1 || (RoomMediaResult)p.ReadByte() != RoomMediaResult.Ok || p.RemainingBytes < 9) return null;
        int target = p.ReadInt();
        bool silenced = p.ReadByte() != 0;
        string leader = p.ReadString();
        if (p.RemainingBytes < 2) return null;
        return new SilenceNotice(target, silenced, leader, p.ReadString());
    }

    public static List<VoicePeer> ReadRoster(Packet p)
    {
        var peers = new List<VoicePeer>();
        if (p.RemainingBytes < 2) return peers;
        int count = Math.Min((int)p.ReadUShort(), RoomMediaWire.PeersMax);
        for (int i = 0; i < count && p.RemainingBytes >= 5; i++)
            peers.Add(new VoicePeer(p.ReadInt(), p.ReadByte()));
        return peers;
    }

    public static VoiceSession? ReadJoined(Packet p)
    {
        if (p.RemainingBytes < 7) return null;
        ushort port = p.ReadUShort();
        uint session = p.ReadUInt();
        int keyLength = p.ReadByte();
        if (keyLength != VoiceSeal.KeyLength || p.RemainingBytes < keyLength + 3) return null;
        byte[] key = p.ReadBytes(keyLength);
        byte self = p.ReadByte();
        int count = Math.Min((int)p.ReadUShort(), RoomMediaWire.PeersMax);
        var peers = new List<VoicePeer>(count);
        for (int i = 0; i < count && p.RemainingBytes >= 5; i++)
            peers.Add(new VoicePeer(p.ReadInt(), p.ReadByte()));
        return new VoiceSession(port, session, key, self, peers);
    }
}

public enum RoomMediaSubOpcode : byte
{
    History = 1,
    Posted = 2,
    Stored = 3,
    React = 4,
    Reactions = 5,
    Kept = 6,
    NoteBegin = 10,
    NoteChunk = 11,
    NoteEnd = 12,
    NoteFetch = 13,
    NoteData = 14,
    VoiceJoin = 20,
    VoiceLeave = 21,
    VoiceState = 22,
    VoiceFrame = 23,
    VoiceTransport = 24,
    VoiceRoster = 25,
    VoiceSilence = 26,
    DirectHistory = 30,
    DirectStored = 31,
}

public enum RoomMemberChange : byte
{
    Joined = 1,
    Left = 2,
    Flags = 3,
}

public enum RoomMediaResult : byte
{
    Ok = 0,
    NotInRoom = 1,
    PublicChannel = 2,
    VoiceOff = 3,
    VoiceFull = 4,
    Silenced = 5,
    TooLong = 6,
    TooBig = 7,
    TooSoon = 8,
    DailyLimit = 9,
    BadUpload = 10,
    NotFound = 11,
    Busy = 12,
    Failed = 13,
    NotLeader = 14,
    PartnerAway = 15,
    Blocked = 16,
    KeptLimit = 17,
}
