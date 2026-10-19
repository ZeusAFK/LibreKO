namespace LibreKO.Session;

public sealed class RoomMediaSession
{
    public RoomTimeline RoomTimeline { get; } = new();

    public VoiceSession? Voice { get; private set; }

    private readonly Dictionary<int, byte> voicePeers = new();

    public IReadOnlyDictionary<int, byte> VoicePeers => voicePeers;

    internal void ApplyVoicePeer(int charId, RoomMemberChange change, byte flags)
    {
        if (change == RoomMemberChange.Left) voicePeers.Remove(charId);
        else voicePeers[charId] = flags;
    }

    internal void ApplyVoiceRoster(IEnumerable<VoicePeer> peers, int selfId)
    {
        voicePeers.Clear();
        foreach (var peer in peers)
            if (peer.CharacterId != selfId) voicePeers[peer.CharacterId] = peer.Flags;
    }

    internal void ClearVoicePeers() => voicePeers.Clear();

    internal void ApplyOwnVoiceFlags(byte flags)
    {
        if (Voice != null) Voice = Voice with { SelfFlags = flags };
    }

    public bool ChatRoomPanelShown { get; private set; }

    internal void ShowRoomPanel() => ChatRoomPanelShown = true;

    internal void HideRoomPanel() => ChatRoomPanelShown = false;

    public bool ChatRoomMembersShown { get; private set; } = true;

    internal void ToggleRoomMembers() => ChatRoomMembersShown = !ChatRoomMembersShown;

    private readonly Dictionary<string, RoomTimeline> directs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<OpenWhisper> openWhispers = new();
    private string whispersOf = "";

    public RoomTimeline DirectTimeline(string partner)
    {
        if (!directs.TryGetValue(partner, out var timeline)) directs[partner] = timeline = new RoomTimeline();
        return timeline;
    }

    public IReadOnlyList<RoomPost> WhisperHistory(string partner) =>
        directs.TryGetValue(partner, out var timeline) ? timeline.Posts : Array.Empty<RoomPost>();

    public void RecordWhisper(string partner, RoomPost post) => DirectTimeline(partner).Add(post);

    public RoomPost? FindChatPost(long id)
    {
        if (RoomTimeline.Find(id) is { } inRoom) return inRoom;
        foreach (var timeline in directs.Values)
            if (timeline.Find(id) is { } direct) return direct;
        return null;
    }

    public string DirectPartnerOf(long id)
    {
        foreach (var (partner, timeline) in directs)
            if (timeline.Find(id) != null) return partner;
        return "";
    }

    public IReadOnlyList<OpenWhisper> OpenWhispers => openWhispers;

    public void KeepOpenWhispers(IEnumerable<OpenWhisper> windows)
    {
        openWhispers.Clear();
        openWhispers.AddRange(windows);
    }

    internal void ForgetWhispersUnlessOf(string character)
    {
        if (string.Equals(whispersOf, character, StringComparison.OrdinalIgnoreCase)) return;
        whispersOf = character;
        directs.Clear();
        openWhispers.Clear();
    }

    public event Action? RoomTimelineEvent;

    internal void RaiseRoomTimeline() => RoomTimelineEvent?.Invoke();

    public event Action<RoomPost>? RoomPostedEvent;

    internal void RaiseRoomPosted(RoomPost roomPost) => RoomPostedEvent?.Invoke(roomPost);

    public event Action<RoomMediaSubOpcode, RoomMediaResult>? RoomMediaResultEvent;

    internal void RaiseRoomMediaResult(RoomMediaSubOpcode arg1, RoomMediaResult arg2) => RoomMediaResultEvent?.Invoke(arg1, arg2);

    public event Action<long, byte[]>? VoiceNoteFetchedEvent;

    internal void RaiseVoiceNoteFetched(long arg1, byte[] arg2) => VoiceNoteFetchedEvent?.Invoke(arg1, arg2);

    public event Action<long>? VoiceNoteMissingEvent;

    internal void RaiseVoiceNoteMissing(long value) => VoiceNoteMissingEvent?.Invoke(value);

    public event Action<VoiceSession>? VoiceJoinedEvent;

    private void RaiseVoiceJoined(VoiceSession voiceSession) => VoiceJoinedEvent?.Invoke(voiceSession);

    public event Action? VoiceLeftEvent;

    private void RaiseVoiceLeft() => VoiceLeftEvent?.Invoke();

    internal void JoinVoice(VoiceSession session)
    {
        Voice = session;
        voicePeers.Clear();
        foreach (var peer in session.Peers) voicePeers[peer.CharacterId] = peer.Flags;
        RaiseVoiceJoined(session);
    }

    internal void LeaveVoice()
    {
        Voice = null;
        RaiseVoiceLeft();
    }

    public event Action<int, RoomMemberChange, byte>? VoicePeerEvent;

    internal void RaiseVoicePeer(int arg1, RoomMemberChange arg2, byte arg3) => VoicePeerEvent?.Invoke(arg1, arg2, arg3);

    public event Action<int, ushort, byte[]>? VoiceTcpFrameEvent;

    internal void RaiseVoiceTcpFrame(int arg1, ushort arg2, byte[] arg3) => VoiceTcpFrameEvent?.Invoke(arg1, arg2, arg3);

    public event Action? VoiceRosterEvent;

    internal void RaiseVoiceRoster() => VoiceRosterEvent?.Invoke();

    public event Action<RoomMediaReplies.SilenceNotice>? VoiceSilenceEvent;

    internal void RaiseVoiceSilence(RoomMediaReplies.SilenceNotice silenceNotice) => VoiceSilenceEvent?.Invoke(silenceNotice);

    public event Action<RoomPost>? ChatPostChangedEvent;

    internal void RaiseChatPostChanged(RoomPost roomPost) => ChatPostChangedEvent?.Invoke(roomPost);

    public event Action<string>? DirectTimelineEvent;

    internal void RaiseDirectTimeline(string value) => DirectTimelineEvent?.Invoke(value);

    public event Action<string, RoomPost>? DirectPostedEvent;

    internal void RaiseDirectPosted(string arg1, RoomPost roomPost) => DirectPostedEvent?.Invoke(arg1, roomPost);

    public string NoteTarget { get; private set; } = "";

    public byte[]? PendingNote { get; private set; }

    internal bool BeginNote(byte[] file, string partner)
    {
        if (PendingNote != null) return false;
        PendingNote = file;
        NoteTarget = partner;
        return true;
    }

    internal void ForgetPendingNote() => PendingNote = null;

    public VoiceNoteDownloads NoteDownloads { get; } = new();

    internal void DropVoice()
    {
        if (Voice == null) return;
        LeaveVoice();
    }

    internal void LeftRoom()
    {
        HideRoomPanel();
        RoomTimeline.Clear();
        ForgetPendingNote();
        NoteDownloads.Clear();
        DropVoice();
        voicePeers.Clear();
        RaiseRoomTimeline();
    }

    internal void Reset()
    {
        RoomTimeline.Clear();
        ForgetPendingNote();
        NoteDownloads.Clear();
        DropVoice();
        voicePeers.Clear();
    }

    public void PreviewVoice(VoiceSession? session, IReadOnlyList<VoicePeer>? others = null)
    {
        Voice = session;
        voicePeers.Clear();
        foreach (var peer in session?.Peers ?? others ?? []) voicePeers[peer.CharacterId] = peer.Flags;
    }
}

public readonly record struct OpenWhisper(string Name, float X, float Y, bool Minimized, bool Unread);
