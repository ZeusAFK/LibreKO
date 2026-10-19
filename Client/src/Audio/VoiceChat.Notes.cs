using Godot;

namespace LibreKO;

public partial class VoiceChat
{
    private const string NoteCacheDir = "user://voice_notes";
    private const double MinNoteSeconds = 0.5;

    public enum NoteState
    {
        Idle,
        Loading,
        Playing,
        Missing,
    }

    private VoiceNoteRecorder? recorder;
    private AudioStreamPlayer? notePlayer;
    private long playingNote;
    private readonly HashSet<long> loadingNotes = new();
    private readonly HashSet<long> missingNotes = new();
    private readonly HashSet<long> decodingNotes = new();
    private (long Note, float Fraction) seekOnPlay;
    private (long Note, float Fraction) previewPlaying;
    private bool netHooked;
    private string noticeTarget = "";

    public bool Recording => recorder != null;
    public double RecordingSeconds => recorder?.Seconds ?? 0;
    public string RecordingTarget { get; private set; } = "";

    public bool RecordingFor(string target) =>
        Recording && string.Equals(RecordingTarget, target, StringComparison.OrdinalIgnoreCase);

    public event Action<byte[], double, byte[], string>? NoteRecorded;
    public event Action<string, string>? NoteNotice;

    public bool StartRecording(string target = "")
    {
        if (Recording || (target.Length == 0 && !nationChat.NationChat.InRoom)) return false;
        noticeTarget = target;
        if (!AskForMicrophone(() => StartRecording(target))) return false;
        recorder = new VoiceNoteRecorder();
        RecordingTarget = target;
        RetainMic(InVoice);
        StateChanged?.Invoke();
        return true;
    }

    public void StopRecording(bool send)
    {
        if (this.recorder is not { } recorder) return;
        this.recorder = null;
        RetainMic(InVoice);
        byte[] file = recorder.Finish();
        double seconds = recorder.Seconds;
        byte[] waveform = recorder.Waveform();
        recorder.Dispose();
        StateChanged?.Invoke();
        if (send && seconds >= MinNoteSeconds) NoteRecorded?.Invoke(file, seconds, waveform, RecordingTarget);
    }

    private void RecordFrame(float[] frame, bool voice)
    {
        if (recorder == null) return;
        recorder.Write(frame, voice);
        if (recorder.Full) StopRecording(send: true);
    }

    public NoteState StateOf(long noteId) =>
        previewPlaying.Note == noteId && noteId != 0 ? NoteState.Playing
        : missingNotes.Contains(noteId) ? NoteState.Missing
        : loadingNotes.Contains(noteId) ? NoteState.Loading
        : playingNote == noteId && notePlayer is { Playing: true } ? NoteState.Playing
        : NoteState.Idle;

    public float NoteProgress(long noteId)
    {
        if (previewPlaying.Note == noteId && noteId != 0) return previewPlaying.Fraction;
        if (playingNote != noteId || notePlayer?.Stream is not AudioStreamWav wav || !notePlayer.Playing) return 0f;
        double length = wav.GetLength();
        return length <= 0 ? 0f : (float)Math.Clamp(notePlayer.GetPlaybackPosition() / length, 0, 1);
    }

    public float LoadProgress(long noteId) =>
        !loadingNotes.Contains(noteId) ? 0f
        : decodingNotes.Contains(noteId) ? 1f
        : previewLoading.Note == noteId ? previewLoading.Fraction
        : roomMedia.NoteDownloads.Progress(noteId);

    private (long Note, float Fraction) previewLoading;

    internal void PreviewNote(long playing, float played, long loading, float loaded)
    {
        previewPlaying = (playing, played);
        previewLoading = (loading, loaded);
        loadingNotes.Clear();
        if (loading != 0) loadingNotes.Add(loading);
    }

    public void SeekNote(long noteId, float fraction)
    {
        if (StateOf(noteId) == NoteState.Playing && notePlayer?.Stream is AudioStreamWav wav)
        {
            notePlayer.Seek((float)(wav.GetLength() * fraction));
            return;
        }
        seekOnPlay = (noteId, fraction);
        if (StateOf(noteId) == NoteState.Idle) ToggleNote(noteId);
    }

    public void ToggleNote(long noteId)
    {
        if (StateOf(noteId) == NoteState.Playing)
        {
            StopNote();
            return;
        }
        if (loadingNotes.Contains(noteId)) return;
        HookNet();
        if (NoteById(noteId) is { } note && CachePath(note) is var cached && File.Exists(cached)
            && new FileInfo(cached).Length == note.Size)
        {
            DecodeAndPlay(noteId, File.ReadAllBytes(cached));
            return;
        }
        missingNotes.Remove(noteId);
        loadingNotes.Add(noteId);
        roomMediaRequests.VoiceNoteFetch(noteId);
        StateChanged?.Invoke();
    }

    public void StopNote()
    {
        notePlayer?.Stop();
        playingNote = 0;
        StateChanged?.Invoke();
    }

    private void HookNet()
    {
        if (netHooked) return;
        netHooked = true;
        roomMedia.VoiceNoteFetchedEvent += OnNoteFetched;
        roomMedia.VoiceNoteMissingEvent += OnNoteMissing;
        TreeExiting += () =>
        {
            roomMedia.VoiceNoteFetchedEvent -= OnNoteFetched;
            roomMedia.VoiceNoteMissingEvent -= OnNoteMissing;
        };
    }

    private void OnNoteFetched(long noteId, byte[] file)
    {
        try
        {
            if (NoteById(noteId) is { } note && file.Length == note.Size)
            {
                string path = CachePath(note);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file);
            }
        }
        catch (IOException ex)
        {
            GD.PushWarning($"[voice] note {noteId} not cached: {ex.Message}");
        }
        DecodeAndPlay(noteId, file);
    }

    private void OnNoteMissing(long noteId)
    {
        loadingNotes.Remove(noteId);
        missingNotes.Add(noteId);
        NoteNotice?.Invoke(roomMedia.DirectPartnerOf(noteId), RoomMediaResults.Text(RoomMediaResult.NotFound));
        StateChanged?.Invoke();
    }

    private void DecodeAndPlay(long noteId, byte[] file)
    {
        loadingNotes.Add(noteId);
        decodingNotes.Add(noteId);
        StateChanged?.Invoke();
        Task.Run(() =>
        {
            short[]? pcm = null;
            try
            {
                pcm = VoiceNoteCodec.Decode(file);
                VoiceNoteCodec.Normalize(pcm);
            }
            catch (Exception ex) { GD.PushWarning($"[voice] note {noteId} could not be decoded: {ex.Message}"); }
            Callable.From(() => Play(noteId, pcm)).CallDeferred();
        });
    }

    private void Play(long noteId, short[]? pcm)
    {
        loadingNotes.Remove(noteId);
        decodingNotes.Remove(noteId);
        if (pcm == null || pcm.Length == 0)
        {
            missingNotes.Add(noteId);
            StateChanged?.Invoke();
            return;
        }
        var bytes = new byte[pcm.Length * sizeof(short)];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            Data = bytes,
        };
        if (notePlayer == null)
        {
            notePlayer = new AudioStreamPlayer { Name = "VoiceNote", Bus = BusVoiceChat };
            notePlayer.Finished += () =>
            {
                playingNote = 0;
                StateChanged?.Invoke();
            };
            AddChild(notePlayer);
        }
        notePlayer.Stream = wav;
        playingNote = noteId;
        float from = seekOnPlay.Note == noteId ? (float)(wav.GetLength() * seekOnPlay.Fraction) : 0f;
        seekOnPlay = default;
        notePlayer.Play(from);
        StateChanged?.Invoke();
    }

    private void ProcessNotes()
    {
        if (recorder != null && RecordingTarget.Length == 0 && !nationChat.NationChat.InRoom) StopRecording(send: false);
    }

    private static string CacheRoot() => ProjectSettings.GlobalizePath(NoteCacheDir);

    private static string CachePath(RoomPost note) => Path.Combine(CacheRoot(), VoiceNoteRules.CacheName(Game.Connection.ServerHost, note));

    private RoomPost? NoteById(long noteId) =>
        roomMedia.FindChatPost(noteId) is { Kind: RoomPostKind.VoiceNote } note ? note : null;

    private static void PruneNoteCache()
    {
        try
        {
            string root = CacheRoot();
            if (!Directory.Exists(root)) return;
            var cutoff = DateTime.UtcNow.AddDays(-VoiceNoteRules.CacheDays);
            foreach (string file in Directory.EnumerateFiles(root, "*" + VoiceNoteRules.CacheExtension, SearchOption.TopDirectoryOnly))
                File.Delete(file);
            foreach (string file in Directory.EnumerateFiles(root, "*" + VoiceNoteRules.CacheExtension, SearchOption.AllDirectories))
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
        }
        catch (IOException ex)
        {
            GD.PushWarning($"[voice] note cache prune failed: {ex.Message}");
        }
    }
}
