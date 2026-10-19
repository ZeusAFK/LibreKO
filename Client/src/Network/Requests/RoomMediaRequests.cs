namespace LibreKO.Network.Requests;

public interface IRoomMediaRequests
{
    void RoomHistory(long beforeId);
    void Reaction(long messageId, string emoji);
    void DirectHistory(string partner, long beforeId);
    void RoomKept(bool keep);
    void VoiceJoin();
    void VoiceSilence(int characterId, bool silence);
    void VoiceRoster();
    void VoiceState(byte flags);
    void VoiceFrame(ushort seq, ReadOnlySpan<byte> opus);
    void VoiceTransport(bool tcp);
    bool SendVoiceNote(byte[] file, double seconds, byte[] waveform, string partner = "");
    void NoteChunks(uint uploadId, byte[] file);
    void VoiceNoteFetch(long noteId);
    void VoiceLeave();
}

public sealed class RoomMediaRequests : IRoomMediaRequests
{
    private readonly IPacketSink sink;
    private readonly NationChatSession nationChat;
    private readonly RoomMediaSession roomMedia;

    public RoomMediaRequests(IPacketSink sink, NationChatSession nationChat, RoomMediaSession roomMedia)
    {
        this.sink = sink;
        this.nationChat = nationChat;
        this.roomMedia = roomMedia;
    }

    public bool SendVoiceNote(byte[] file, double seconds, byte[] waveform, string partner = "")
    {
        if ((partner.Length == 0 && !nationChat.NationChat.InRoom) || file.Length == 0 || file.Length > VoiceNoteRules.MaxBytes) return false;
        if (!roomMedia.BeginNote(file, partner)) return false;
        var p = MediaPacket(RoomMediaSubOpcode.NoteBegin);
        p.WriteUShort((ushort)Math.Clamp((int)Math.Round(seconds * VoiceNoteRules.DecisecondsPerSecond), 0, ushort.MaxValue));
        p.WriteUInt((uint)file.Length);
        int samples = Math.Min(waveform.Length, VoiceWaveform.Samples);
        p.WriteByte((byte)samples);
        p.WriteBytes(waveform[..samples]);
        if (partner.Length > 0) p.WriteString(partner);
        sink.Send(p);
        return true;
    }

    public void RoomHistory(long beforeId)
    {
        if (!nationChat.NationChat.InRoom) return;
        var p = MediaPacket(RoomMediaSubOpcode.History);
        p.WriteLong(beforeId);
        sink.Send(p);
    }

    public void Reaction(long messageId, string emoji)
    {
        if (messageId <= 0 || (emoji.Length > 0 && !ReactionRules.Valid(emoji))) return;
        var p = MediaPacket(RoomMediaSubOpcode.React);
        p.WriteLong(messageId);
        p.WriteString(emoji);
        sink.Send(p);
    }

    public void DirectHistory(string partner, long beforeId)
    {
        if (partner.Length == 0) return;
        var p = MediaPacket(RoomMediaSubOpcode.DirectHistory);
        p.WriteString(partner);
        p.WriteLong(beforeId);
        sink.Send(p);
    }

    public void RoomKept(bool keep)
    {
        if (!nationChat.NationChat.InPrivate) return;
        var p = MediaPacket(RoomMediaSubOpcode.Kept);
        p.WriteByte((byte)(keep ? 1 : 0));
        sink.Send(p);
    }

    public void VoiceJoin() => sink.Send(MediaPacket(RoomMediaSubOpcode.VoiceJoin));

    public void VoiceSilence(int characterId, bool silence)
    {
        var p = MediaPacket(RoomMediaSubOpcode.VoiceSilence);
        p.WriteInt(characterId);
        p.WriteByte((byte)(silence ? 1 : 0));
        sink.Send(p);
    }

    public void VoiceRoster()
    {
        if (nationChat.NationChat.InPrivate) sink.Send(MediaPacket(RoomMediaSubOpcode.VoiceRoster));
    }

    public void VoiceState(byte flags)
    {
        if (roomMedia.Voice == null) return;
        var p = MediaPacket(RoomMediaSubOpcode.VoiceState);
        p.WriteByte(flags);
        sink.Send(p);
    }

    public void VoiceFrame(ushort seq, ReadOnlySpan<byte> opus)
    {
        if (roomMedia.Voice == null || opus.Length == 0) return;
        var p = MediaPacket(RoomMediaSubOpcode.VoiceFrame);
        p.WriteUShort(seq);
        p.WriteUShort((ushort)opus.Length);
        p.WriteBytes(opus.ToArray());
        sink.Send(p);
    }

    public void VoiceTransport(bool tcp)
    {
        if (roomMedia.Voice == null) return;
        var p = MediaPacket(RoomMediaSubOpcode.VoiceTransport);
        p.WriteByte((byte)(tcp ? 1 : 0));
        sink.Send(p);
    }

    public void NoteChunks(uint uploadId, byte[] file)
    {
        ushort index = 0;
        for (int offset = 0; offset < file.Length; offset += VoiceNoteRules.ChunkBytes, index++)
        {
            int length = Math.Min(VoiceNoteRules.ChunkBytes, file.Length - offset);
            var chunk = MediaPacket(RoomMediaSubOpcode.NoteChunk);
            chunk.WriteUInt(uploadId);
            chunk.WriteUShort(index);
            chunk.WriteUShort((ushort)length);
            chunk.WriteBytes(file[offset..(offset + length)]);
            sink.Send(chunk);
        }
        var end = MediaPacket(RoomMediaSubOpcode.NoteEnd);
        end.WriteUInt(uploadId);
        sink.Send(end);
    }

    public void VoiceNoteFetch(long noteId)
    {
        if (roomMedia.NoteDownloads.Pending(noteId)) return;
        var p = MediaPacket(RoomMediaSubOpcode.NoteFetch);
        p.WriteLong(noteId);
        sink.Send(p);
    }

    public void VoiceLeave()
    {
        if (roomMedia.Voice == null) return;
        sink.Send(MediaPacket(RoomMediaSubOpcode.VoiceLeave));
        roomMedia.DropVoice();
    }

    private static Packet MediaPacket(RoomMediaSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_ROOM_MEDIA);
        p.WriteByte((byte)sub);
        return p;
    }
}
