using System;
using System.Collections.Generic;
using System.Linq;

namespace LibreKO.Domain;

public static class RoomMediaResults
{
    public static string Text(RoomMediaResult result) => result switch
    {
        RoomMediaResult.PartnerAway => "That player is not online.",
        RoomMediaResult.Blocked => "Private messages are blocked.",
        RoomMediaResult.KeptLimit => "You already keep as many rooms as you can.",
        RoomMediaResult.NotInRoom => "You are not in a chat room.",
        RoomMediaResult.PublicChannel => "Voice calls are available in private rooms.",
        RoomMediaResult.VoiceOff => "Voice chat is turned off on this server.",
        RoomMediaResult.VoiceFull => "The voice call in this room is full.",
        RoomMediaResult.Silenced => "You cannot speak while you are muted.",
        RoomMediaResult.TooLong => $"Voice notes can be up to {VoiceNoteRules.MaxSeconds} seconds.",
        RoomMediaResult.TooBig => "That voice note is too large.",
        RoomMediaResult.TooSoon => "Wait a moment before sending another voice note.",
        RoomMediaResult.DailyLimit => "You have sent too many voice notes today.",
        RoomMediaResult.NotFound => "That voice note is no longer available.",
        RoomMediaResult.Failed => "The voice note could not be saved.",
        RoomMediaResult.NotLeader => "Only the room leader can do that.",
        RoomMediaResult.Ok => "",
        _ => "The voice note could not be sent.",
    };
}

public static class VoiceFlags
{
    public const byte MicMuted = 0x01;
    public const byte Deafened = 0x02;
    public const byte CannotSpeak = 0x04;
    public const byte Silenced = 0x08;
    public const byte NoMicrophone = MicMuted | CannotSpeak | Silenced;
}

public readonly record struct RosterEntry(int CharacterId, string Name, bool InVoice);

public static class ChatRoomRoster
{
    public static List<RosterEntry> Arrange(IEnumerable<(int CharacterId, string Name)> members, IReadOnlyCollection<int> inVoice, Func<int, string> nameOf)
    {
        var names = new Dictionary<int, string>();
        foreach (var (id, name) in members) names[id] = name;
        foreach (int id in inVoice)
            if (!names.ContainsKey(id)) names[id] = nameOf(id);
        return names
            .Select(pair => new RosterEntry(pair.Key, pair.Value, inVoice.Contains(pair.Key)))
            .OrderByDescending(entry => entry.InVoice)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.CharacterId)
            .ToList();
    }
}

public static class VoiceNoteRules
{
    public const int MaxSeconds = 60;
    public const int MaxBytes = 320 * 1024;
    public const int ChunkBytes = 8192;
    public const int CacheDays = 90;
    public const int DecisecondsPerSecond = 10;
    public const string CacheExtension = ".opus";
    private const string LocalHostFolder = "local";

    public static string CacheName(string host, RoomPost note)
    {
        string folder = new(host.Trim().Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_').ToArray());
        long unix = new DateTimeOffset(note.Time.ToUniversalTime()).ToUnixTimeSeconds();
        return $"{(folder.Length > 0 ? folder : LocalHostFolder)}/{note.Id}_{unix}_{note.Size}{CacheExtension}";
    }

    public static string Clock(double seconds)
    {
        int whole = Math.Max(0, (int)Math.Round(seconds));
        return $"{whole / 60}:{whole % 60:00}";
    }
}

public enum RoomPostKind : byte
{
    Text = 0,
    VoiceNote = 1,
    Notice = 2,
}

public sealed record RoomPost(long Id, RoomPostKind Kind, int SenderId, string Name, DateTime Time, string Text, int DurationDs, int Size)
{
    public long Id { get; set; } = Id;
    public byte[] Waveform { get; init; } = [];
    public MessageReactions Reactions { get; } = new();

    public static RoomPost Line(int senderId, string name, string text, DateTime time) =>
        new(0, RoomPostKind.Text, senderId, name, time, text, 0, 0);

    public static RoomPost Notice(string text, DateTime time) =>
        new(0, RoomPostKind.Notice, 0, "", time, text, 0, 0);

    public double Seconds => DurationDs / (double)VoiceNoteRules.DecisecondsPerSecond;
}

public sealed class RoomTimeline
{
    public const int MaxPosts = 400;
    public static readonly TimeSpan GroupGap = TimeSpan.FromMinutes(5);

    private readonly List<RoomPost> posts = [];
    private readonly List<RoomPost> early = [];

    public IReadOnlyList<RoomPost> Posts => posts;
    public bool HistoryLoaded { get; private set; }
    public bool MoreHistory { get; private set; }

    public long OldestId => posts.Where(p => p.Id > 0).Select(p => p.Id).DefaultIfEmpty(0).Min();

    public void Clear()
    {
        posts.Clear();
        early.Clear();
        HistoryLoaded = false;
        MoreHistory = false;
    }

    public void Add(RoomPost post)
    {
        if (post.Id > 0 && posts.Any(p => p.Id == post.Id)) return;
        if (!HistoryLoaded && post.Kind == RoomPostKind.Text) early.Add(post);
        posts.Add(post);
        Trim();
    }

    public RoomPost? Find(long id)
    {
        if (id <= 0) return null;
        for (int i = posts.Count - 1; i >= 0; i--)
            if (posts[i].Id == id) return posts[i];
        return null;
    }

    public (RoomPost Post, bool Added) Stored(RoomPost row)
    {
        if (Find(row.Id) is { } known) return (known, false);
        if (row.Kind == RoomPostKind.Text)
        {
            var waiting = posts.Where(p => p.Id == 0 && p.Kind == RoomPostKind.Text && p.SenderId == row.SenderId).ToList();
            if ((waiting.FirstOrDefault(p => p.Text == row.Text) ?? waiting.FirstOrDefault()) is { } line)
            {
                line.Id = row.Id;
                return (line, false);
            }
        }
        Add(row);
        return (row, true);
    }

    public int MergeHistory(IReadOnlyList<RoomPost> page, bool more)
    {
        var known = new HashSet<long>(posts.Where(p => p.Id > 0).Select(p => p.Id));
        var fresh = page.Where(p => !known.Contains(p.Id)).ToList();
        if (!HistoryLoaded)
        {
            foreach (var earlyPost in early)
                if (fresh.Any(p => p.Kind == RoomPostKind.Text && p.SenderId == earlyPost.SenderId && p.Text == earlyPost.Text))
                    posts.Remove(earlyPost);
            early.Clear();
            HistoryLoaded = true;
        }
        posts.InsertRange(0, fresh);
        MoreHistory = more;
        Trim();
        return fresh.Count;
    }

    public bool StartsGroup(int index)
    {
        if (index <= 0 || index >= posts.Count) return true;
        var post = posts[index];
        var before = posts[index - 1];
        if (post.Kind == RoomPostKind.Notice || before.Kind == RoomPostKind.Notice) return true;
        return post.SenderId != before.SenderId || post.Time - before.Time > GroupGap;
    }

    private void Trim()
    {
        if (posts.Count > MaxPosts) posts.RemoveRange(0, posts.Count - MaxPosts);
    }
}

public static class RoomNameColours
{
    public static readonly string[] Palette =
    [
        "e57373", "f06292", "ba68c8", "9575cd", "7986cb", "64b5f6",
        "4dd0e1", "4db6ac", "81c784", "dce775", "ffd54f", "ffb74d",
    ];

    public static string Of(int characterId) => Palette[(int)((uint)characterId % (uint)Palette.Length)];
}

public sealed class VoiceNoteDownloads
{
    private sealed class Download(int size)
    {
        public byte[] Data { get; } = new byte[size];
        public int Received { get; set; }
    }

    private readonly Dictionary<long, Download> downloads = new();

    public bool Pending(long noteId) => downloads.ContainsKey(noteId);

    public float Progress(long noteId) =>
        downloads.TryGetValue(noteId, out var download) ? download.Received / (float)download.Data.Length : 0f;

    public bool Start(long noteId, int size)
    {
        downloads.Remove(noteId);
        if (size <= 0 || size > VoiceNoteRules.MaxBytes) return false;
        downloads[noteId] = new Download(size);
        return true;
    }

    public byte[]? Append(long noteId, byte[] data)
    {
        if (!downloads.TryGetValue(noteId, out var download)) return null;
        if (download.Received + data.Length > download.Data.Length)
        {
            downloads.Remove(noteId);
            return null;
        }
        data.CopyTo(download.Data, download.Received);
        download.Received += data.Length;
        if (download.Received < download.Data.Length) return null;
        downloads.Remove(noteId);
        return download.Data;
    }

    public void Clear() => downloads.Clear();
}
