namespace LibreKO.Network.Readers;

internal sealed class RoomMediaReader : IPacketReader
{
    private readonly NationChatSession nationChat;
    private readonly RoomMediaSession roomMedia;
    private readonly SelfSession self;
    private readonly IRoomMediaRequests requests;

    public RoomMediaReader(NationChatSession nationChat, RoomMediaSession roomMedia, SelfSession self, IRoomMediaRequests requests)
    {
        this.nationChat = nationChat;
        this.roomMedia = roomMedia;
        this.self = self;
        this.requests = requests;
    }

    public void Register(PacketDispatcher dispatcher)
    {
        dispatcher.On(GameOpcodes.GS_ROOM_MEDIA, HandleRoomMedia);
    }

    private void ApplyReaction(RoomMediaReplies.ReactionChange change)
    {
        if (roomMedia.FindChatPost(change.MessageId) is { } post && post.Reactions.Set(change.CharacterId, change.Name, change.Emoji))
            roomMedia.RaiseChatPostChanged(post);
    }

    private void HandleRoomMedia(Packet p)
    {
        if (p.RemainingBytes < 1) return;
        var sub = (RoomMediaSubOpcode)p.ReadByte();
        switch (sub)
        {
            case RoomMediaSubOpcode.History:
                {
                    var (rows, more) = RoomMediaReplies.ReadHistory(p);
                    roomMedia.RoomTimeline.MergeHistory(rows, more);
                    roomMedia.RaiseRoomTimeline();
                    return;
                }
            case RoomMediaSubOpcode.Posted:
                if (RoomMediaReplies.ReadRow(p) is { } posted)
                {
                    roomMedia.RoomTimeline.Add(posted);
                    roomMedia.RaiseRoomPosted(posted);
                }
                return;
            case RoomMediaSubOpcode.Stored:
                if (RoomMediaReplies.ReadRow(p) is { } stored && nationChat.NationChat.InRoom)
                {
                    var (post, added) = roomMedia.RoomTimeline.Stored(stored);
                    if (added) roomMedia.RaiseRoomPosted(post);
                    else roomMedia.RaiseChatPostChanged(post);
                }
                return;
            case RoomMediaSubOpcode.React:
                if (RoomMediaReplies.ReadReaction(p) is { } reaction) ApplyReaction(reaction);
                return;
            case RoomMediaSubOpcode.Reactions:
                foreach (var each in RoomMediaReplies.ReadReactions(p)) ApplyReaction(each);
                return;
            case RoomMediaSubOpcode.Kept:
                if (RoomMediaReplies.ReadKept(p) is not { } kept) return;
                if ((RoomMediaResult)kept.Result == RoomMediaResult.Ok) nationChat.NationChat.KeptChanged(kept.Kept);
                else roomMedia.RaiseRoomMediaResult(sub, (RoomMediaResult)kept.Result);
                nationChat.RaiseNationChatState();
                return;
            case RoomMediaSubOpcode.DirectHistory:
                {
                    var (partner, rows, more) = RoomMediaReplies.ReadDirectHistory(p);
                    if (partner.Length == 0) return;
                    roomMedia.DirectTimeline(partner).MergeHistory(rows, more);
                    roomMedia.RaiseDirectTimeline(partner);
                    return;
                }
            case RoomMediaSubOpcode.DirectStored:
                if (RoomMediaReplies.ReadDirectRow(p) is { } direct)
                {
                    var (post, added) = roomMedia.DirectTimeline(direct.Partner).Stored(direct.Row);
                    if (added) roomMedia.RaiseDirectPosted(direct.Partner, post);
                    else roomMedia.RaiseChatPostChanged(post);
                }
                return;
            case RoomMediaSubOpcode.NoteBegin:
                {
                    var result = (RoomMediaResult)ReadMediaResult(p);
                    if (result == RoomMediaResult.Ok && p.RemainingBytes >= 4 && roomMedia.PendingNote is { } note) requests.NoteChunks(p.ReadUInt(), note);
                    else roomMedia.RaiseRoomMediaResult(sub, result);
                    if (result != RoomMediaResult.Ok) roomMedia.ForgetPendingNote();
                    return;
                }
            case RoomMediaSubOpcode.NoteEnd:
                roomMedia.ForgetPendingNote();
                roomMedia.RaiseRoomMediaResult(sub, (RoomMediaResult)ReadMediaResult(p));
                return;
            case RoomMediaSubOpcode.NoteFetch:
                ReadNoteHeader(p);
                return;
            case RoomMediaSubOpcode.NoteData:
                ReadNoteChunk(p);
                return;
            case RoomMediaSubOpcode.VoiceJoin:
                {
                    var result = (RoomMediaResult)ReadMediaResult(p);
                    if (result == RoomMediaResult.Ok && RoomMediaReplies.ReadJoined(p) is { } session)
                    {
                        roomMedia.JoinVoice(session);
                    }
                    else roomMedia.RaiseRoomMediaResult(sub, result);
                    return;
                }
            case RoomMediaSubOpcode.VoiceLeave:
                ReadMediaResult(p);
                roomMedia.DropVoice();
                return;
            case RoomMediaSubOpcode.VoiceState:
                if (RoomMediaReplies.ReadVoiceChange(p) is not { } change) return;
                if (change.CharacterId == self.MyCharId) roomMedia.ApplyOwnVoiceFlags(change.Flags);
                else roomMedia.ApplyVoicePeer(change.CharacterId, change.Change, change.Flags);
                roomMedia.RaiseVoicePeer(change.CharacterId, change.Change, change.Flags);
                return;
            case RoomMediaSubOpcode.VoiceRoster:
                roomMedia.ApplyVoiceRoster(RoomMediaReplies.ReadRoster(p), self.MyCharId);
                roomMedia.RaiseVoiceRoster();
                return;
            case RoomMediaSubOpcode.VoiceSilence:
                if (RoomMediaReplies.ReadSilenceNotice(p) is { } silence) roomMedia.RaiseVoiceSilence(silence);
                return;
            case RoomMediaSubOpcode.VoiceFrame:
                if (RoomMediaReplies.ReadVoiceFrame(p) is { } frame)
                    roomMedia.RaiseVoiceTcpFrame(frame.Speaker, frame.Seq, frame.Opus);
                return;
        }
    }

    private static byte ReadMediaResult(Packet p) => p.RemainingBytes >= 1 ? p.ReadByte() : (byte)RoomMediaResult.Failed;

    private void ReadNoteHeader(Packet p)
    {
        if (RoomMediaReplies.ReadNoteHeader(p) is not { } header) return;
        if (header.Result != RoomMediaResult.Ok || !roomMedia.NoteDownloads.Start(header.NoteId, header.Size))
            roomMedia.RaiseVoiceNoteMissing(header.NoteId);
    }

    private void ReadNoteChunk(Packet p)
    {
        if (RoomMediaReplies.ReadNoteChunk(p) is { } chunk && roomMedia.NoteDownloads.Append(chunk.NoteId, chunk.Data) is { } file)
            roomMedia.RaiseVoiceNoteFetched(chunk.NoteId, file);
    }
}
