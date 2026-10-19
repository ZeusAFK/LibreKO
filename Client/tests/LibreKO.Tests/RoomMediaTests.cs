using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class RoomMediaTests
{
    private static readonly DateTime Noon = new(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    private static readonly uint NoonUnix = (uint)new DateTimeOffset(Noon).ToUnixTimeSeconds();

    private static Packet Media(RoomMediaSubOpcode sub)
    {
        var p = new Packet(GameOpcodes.GS_ROOM_MEDIA);
        p.WriteByte((byte)sub);
        return p;
    }

    private static Packet Ready(Packet p)
    {
        p.ResetOffset();
        p.ReadByte();
        return p;
    }

    private static void WriteTextRow(Packet p, long id, int sender, string name, string text)
    {
        p.WriteLong(id);
        p.WriteByte((byte)RoomPostKind.Text);
        p.WriteInt(sender);
        p.WriteString(name);
        p.WriteUInt(NoonUnix);
        p.WriteString(text);
    }

    private static readonly byte[] Wave = Enumerable.Range(0, VoiceWaveform.Samples).Select(i => (byte)(i * 4)).ToArray();

    private static void WriteNoteRow(Packet p, long id, int sender, string name, ushort durationDs, uint size)
    {
        p.WriteLong(id);
        p.WriteByte((byte)RoomPostKind.VoiceNote);
        p.WriteInt(sender);
        p.WriteString(name);
        p.WriteUInt(NoonUnix);
        p.WriteUShort(durationDs);
        p.WriteUInt(size);
        p.WriteByte((byte)Wave.Length);
        p.WriteBytes(Wave);
    }

    [Fact]
    public void AHistoryPageReadsTextAndNoteRowsAndTheMoreFlag()
    {
        var p = Media(RoomMediaSubOpcode.History);
        p.WriteByte(2);
        WriteTextRow(p, 41, 7, "Rikka", "Morning 🙏");
        WriteNoteRow(p, 42, 9, "Borin", 124, 31000);
        p.WriteByte(1);

        var ready = Ready(p);
        var (rows, more) = RoomMediaReplies.ReadHistory(ready);

        Assert.True(more);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new RoomPost(41, RoomPostKind.Text, 7, "Rikka", Noon.ToLocalTime(), "Morning 🙏", 0, 0), rows[0]);
        Assert.Equal(RoomPostKind.VoiceNote, rows[1].Kind);
        Assert.Equal(42, rows[1].Id);
        Assert.Equal(12.4, rows[1].Seconds, 3);
        Assert.Equal(31000, rows[1].Size);
        Assert.Equal(Wave, rows[1].Waveform);
        Assert.Equal(0, ready.RemainingBytes);
    }

    [Fact]
    public void APostedRowIsTheSameRowShape()
    {
        var p = Media(RoomMediaSubOpcode.Posted);
        WriteNoteRow(p, 77, 3, "Arwen", 47, 12000);

        var row = RoomMediaReplies.ReadRow(Ready(p));

        Assert.NotNull(row);
        Assert.Equal(77, row!.Id);
        Assert.Equal("Arwen", row.Name);
        Assert.Equal(47, row.DurationDs);
        Assert.Equal(0, p.RemainingBytes);
    }

    [Fact]
    public void TheVoiceJoinReplyCarriesPortSessionKeyAndPeers()
    {
        var key = Enumerable.Range(1, VoiceSeal.KeyLength).Select(i => (byte)i).ToArray();
        var p = Media(RoomMediaSubOpcode.VoiceJoin);
        p.WriteByte((byte)RoomMediaResult.Ok);
        p.WriteUShort(15002);
        p.WriteUInt(0xA1B2C3D4);
        p.WriteByte((byte)key.Length);
        p.WriteBytes(key);
        p.WriteByte(VoiceFlags.CannotSpeak);
        p.WriteUShort(2);
        p.WriteInt(1001);
        p.WriteByte(0);
        p.WriteInt(1002);
        p.WriteByte(VoiceFlags.MicMuted | VoiceFlags.Deafened);

        var ready = Ready(p);
        Assert.Equal((byte)RoomMediaResult.Ok, ready.ReadByte());
        var session = RoomMediaReplies.ReadJoined(ready);

        Assert.NotNull(session);
        Assert.Equal(15002, session!.UdpPort);
        Assert.Equal(0xA1B2C3D4, session.SessionId);
        Assert.Equal(key, session.Key);
        Assert.Equal(VoiceFlags.CannotSpeak, session.SelfFlags);
        Assert.Equal([new VoicePeer(1001, 0), new VoicePeer(1002, VoiceFlags.MicMuted | VoiceFlags.Deafened)], session.Peers);
        Assert.Equal(0, ready.RemainingBytes);
    }

    [Fact]
    public void TheVoiceRosterListsWhoIsInTheCall()
    {
        var p = Media(RoomMediaSubOpcode.VoiceRoster);
        p.WriteUShort(2);
        p.WriteInt(1001);
        p.WriteByte(0);
        p.WriteInt(1002);
        p.WriteByte(VoiceFlags.MicMuted);

        var ready = Ready(p);
        Assert.Equal([new VoicePeer(1001, 0), new VoicePeer(1002, VoiceFlags.MicMuted)], RoomMediaReplies.ReadRoster(ready));
        Assert.Equal(0, ready.RemainingBytes);
    }

    [Fact]
    public void AJoinReplyWithAWrongKeyLengthIsRefused()
    {
        var p = Media(RoomMediaSubOpcode.VoiceJoin);
        p.WriteByte((byte)RoomMediaResult.Ok);
        p.WriteUShort(15002);
        p.WriteUInt(1);
        p.WriteByte(8);
        p.WriteBytes(new byte[8]);
        p.WriteByte(0);
        p.WriteUShort(0);
        var ready = Ready(p);
        ready.ReadByte();
        Assert.Null(RoomMediaReplies.ReadJoined(ready));
    }

    [Fact]
    public void NoteFetchHeaderAndChunksAssembleTheFile()
    {
        var file = Enumerable.Range(0, 20000).Select(i => (byte)(i * 7)).ToArray();
        var header = Media(RoomMediaSubOpcode.NoteFetch);
        header.WriteByte((byte)RoomMediaResult.Ok);
        header.WriteLong(55);
        header.WriteUInt((uint)file.Length);
        var parsed = RoomMediaReplies.ReadNoteHeader(Ready(header));
        Assert.Equal(new RoomMediaReplies.NoteHeader(RoomMediaResult.Ok, 55, file.Length), parsed);

        var downloads = new VoiceNoteDownloads();
        Assert.True(downloads.Start(55, file.Length));
        byte[]? complete = null;
        ushort index = 0;
        for (int offset = 0; offset < file.Length; offset += VoiceNoteRules.ChunkBytes, index++)
        {
            int length = Math.Min(VoiceNoteRules.ChunkBytes, file.Length - offset);
            var chunk = Media(RoomMediaSubOpcode.NoteData);
            chunk.WriteLong(55);
            chunk.WriteUShort(index);
            chunk.WriteUShort((ushort)length);
            chunk.WriteBytes(file[offset..(offset + length)]);
            var read = RoomMediaReplies.ReadNoteChunk(Ready(chunk));
            Assert.Equal(index, read!.Value.Index);
            complete = downloads.Append(read.Value.NoteId, read.Value.Data);
            Assert.Equal(offset + length == file.Length, complete != null);
        }
        Assert.Equal(file, complete);
        Assert.False(downloads.Pending(55));
    }

    [Fact]
    public void DownloadProgressCountsTheBytesReceived()
    {
        var downloads = new VoiceNoteDownloads();
        Assert.Equal(0f, downloads.Progress(7));
        downloads.Start(7, 1000);
        downloads.Append(7, new byte[250]);
        Assert.Equal(0.25f, downloads.Progress(7));
        downloads.Append(7, new byte[750]);
        Assert.Equal(0f, downloads.Progress(7));
    }

    [Fact]
    public void ARefusedFetchHasNoSizeAndAnOversizedOneIsNotStarted()
    {
        var refused = Media(RoomMediaSubOpcode.NoteFetch);
        refused.WriteByte((byte)RoomMediaResult.NotFound);
        refused.WriteLong(9);
        Assert.Equal(new RoomMediaReplies.NoteHeader(RoomMediaResult.NotFound, 9, 0), RoomMediaReplies.ReadNoteHeader(Ready(refused)));
        Assert.False(new VoiceNoteDownloads().Start(9, VoiceNoteRules.MaxBytes + 1));
    }

    [Fact]
    public void TheSilenceNoticeNamesTheLeaderAndTheTarget()
    {
        var notice = Media(RoomMediaSubOpcode.VoiceSilence);
        notice.WriteByte((byte)RoomMediaResult.Ok);
        notice.WriteInt(1002);
        notice.WriteByte(1);
        notice.WriteString("Tester10");
        notice.WriteString("Borin");

        var read = RoomMediaReplies.ReadSilenceNotice(Ready(notice));

        Assert.Equal(new RoomMediaReplies.SilenceNotice(1002, true, "Tester10", "Borin"), read);
        Assert.Equal("Tester10 silenced Borin's microphone.", read!.Value.Text);
        Assert.Equal("Tester10 let Borin speak again.", (read.Value with { Silenced = false }).Text);
        var refused = Media(RoomMediaSubOpcode.VoiceSilence);
        refused.WriteByte((byte)RoomMediaResult.NotLeader);
        Assert.Null(RoomMediaReplies.ReadSilenceNotice(Ready(refused)));
    }

    [Fact]
    public void SavedRoomOptionsReplaceTheRoomStateButNeverShrinkBelowTheMembers()
    {
        var state = new NationChatState();
        state.OwnRoom(5, "Old", true, "1234", 30);
        state.Counted(12);

        state.Configured("New", false, "1234", 10);

        Assert.Equal(("New", false, "", 12), (state.RoomName, state.RoomPrivate, state.RoomPassword, state.RoomMax));
    }

    [Fact]
    public void VoiceStateAndTcpFramesRead()
    {
        var state = Media(RoomMediaSubOpcode.VoiceState);
        state.WriteInt(1002);
        state.WriteByte((byte)RoomMemberChange.Flags);
        state.WriteByte(VoiceFlags.MicMuted);
        Assert.Equal(new RoomMediaReplies.VoiceChange(1002, RoomMemberChange.Flags, VoiceFlags.MicMuted),
            RoomMediaReplies.ReadVoiceChange(Ready(state)));

        var frame = Media(RoomMediaSubOpcode.VoiceFrame);
        frame.WriteInt(1);
        frame.WriteUShort(77);
        frame.WriteUShort(3);
        frame.WriteBytes([9, 8, 7]);
        var read = RoomMediaReplies.ReadVoiceFrame(Ready(frame));
        Assert.Equal(1, read!.Value.Speaker);
        Assert.Equal(77, read.Value.Seq);
        Assert.Equal(new byte[] { 9, 8, 7 }, read.Value.Opus);
    }

    [Fact]
    public void TheTimelineDropsAnEarlyLiveLineThatHistoryAlsoCarries()
    {
        var timeline = new RoomTimeline();
        timeline.Add(RoomPost.Line(7, "Rikka", "hello", Noon));
        timeline.MergeHistory(
        [
            new RoomPost(10, RoomPostKind.Text, 3, "Borin", Noon.AddMinutes(-9), "older", 0, 0),
            new RoomPost(11, RoomPostKind.Text, 7, "Rikka", Noon, "hello", 0, 0),
        ], more: true);

        Assert.Equal(["older", "hello"], timeline.Posts.Select(p => p.Text));
        Assert.True(timeline.MoreHistory);
        Assert.Equal(10, timeline.OldestId);

        timeline.Add(RoomPost.Line(7, "Rikka", "hello", Noon.AddMinutes(1)));
        Assert.Equal(3, timeline.Posts.Count);
    }

    [Fact]
    public void OlderPagesGoOnTopAndPostedNotesAreNotDoubled()
    {
        var timeline = new RoomTimeline();
        timeline.MergeHistory([new RoomPost(20, RoomPostKind.Text, 1, "A", Noon, "new", 0, 0)], more: true);
        timeline.Add(new RoomPost(21, RoomPostKind.VoiceNote, 1, "A", Noon, "", 30, 900));
        timeline.Add(new RoomPost(21, RoomPostKind.VoiceNote, 1, "A", Noon, "", 30, 900));
        timeline.MergeHistory([new RoomPost(19, RoomPostKind.Text, 2, "B", Noon.AddMinutes(-1), "old", 0, 0)], more: false);

        Assert.Equal([19L, 20L, 21L], timeline.Posts.Select(p => p.Id));
        Assert.False(timeline.MoreHistory);
    }

    [Fact]
    public void GroupsBreakOnANewSenderAGapOrANotice()
    {
        var timeline = new RoomTimeline();
        timeline.MergeHistory([], more: false);
        timeline.Add(RoomPost.Line(1, "A", "one", Noon));
        timeline.Add(RoomPost.Line(1, "A", "two", Noon.AddMinutes(1)));
        timeline.Add(RoomPost.Line(2, "B", "three", Noon.AddMinutes(2)));
        timeline.Add(RoomPost.Line(2, "B", "four", Noon.AddMinutes(2) + RoomTimeline.GroupGap + TimeSpan.FromSeconds(1)));
        timeline.Add(RoomPost.Notice("B has left.", Noon.AddMinutes(30)));
        timeline.Add(RoomPost.Line(2, "B", "five", Noon.AddMinutes(31)));

        Assert.Equal([true, false, true, true, true, true], Enumerable.Range(0, 6).Select(timeline.StartsGroup));
    }

    [Fact]
    public void TheTimelineKeepsTheNewestPostsOnly()
    {
        var timeline = new RoomTimeline();
        timeline.MergeHistory([], more: false);
        for (int i = 0; i < RoomTimeline.MaxPosts + 5; i++) timeline.Add(RoomPost.Line(1, "A", $"{i}", Noon));
        Assert.Equal(RoomTimeline.MaxPosts, timeline.Posts.Count);
        Assert.Equal("5", timeline.Posts[0].Text);
    }

    [Fact]
    public void NameColoursAreStablePerCharacterAndHandleAnyId()
    {
        Assert.Equal(RoomNameColours.Of(1001), RoomNameColours.Of(1001));
        Assert.NotEqual(RoomNameColours.Of(1001), RoomNameColours.Of(1002));
        Assert.Contains(RoomNameColours.Of(-5), RoomNameColours.Palette);
        Assert.Equal(RoomNameColours.Palette.Length, RoomNameColours.Palette.Distinct().Count());
    }

    [Theory]
    [InlineData(0.0, "0:00")]
    [InlineData(4.7, "0:05")]
    [InlineData(59.6, "1:00")]
    [InlineData(61, "1:01")]
    public void NoteClocksReadAsMinutesAndSeconds(double seconds, string expected) =>
        Assert.Equal(expected, VoiceNoteRules.Clock(seconds));

    [Fact]
    public void EveryRefusalHasAMessage()
    {
        for (var code = RoomMediaResult.NotInRoom; code <= RoomMediaResult.Failed; code++)
            Assert.NotEmpty(RoomMediaResults.Text(code));
        Assert.Empty(RoomMediaResults.Text(RoomMediaResult.Ok));
    }

    [Fact]
    public void NotesAreCachedPerServerAndNotByNumberAlone()
    {
        var time = new DateTime(2026, 10, 11, 22, 27, 0, DateTimeKind.Local);
        var note = new RoomPost(13, RoomPostKind.VoiceNote, 4, "prueba", time, "", 52, 8220);

        string live = VoiceNoteRules.CacheName("libreko.org", note);

        Assert.StartsWith("libreko.org/13_", live);
        Assert.EndsWith("_8220" + VoiceNoteRules.CacheExtension, live);
        Assert.NotEqual(live, VoiceNoteRules.CacheName("127.0.0.1", note));
        Assert.NotEqual(live, VoiceNoteRules.CacheName("libreko.org", note with { Size = 8221 }));
        Assert.NotEqual(live, VoiceNoteRules.CacheName("libreko.org", note with { Time = time.AddSeconds(1) }));
        Assert.StartsWith("my_host_15001/", VoiceNoteRules.CacheName("my host:15001", note));
        Assert.StartsWith("local/", VoiceNoteRules.CacheName("", note));
    }

    [Theory]
    [InlineData("old.example", "old.example", "new.example")]
    [InlineData(" OLD.example ", "old.example", "new.example")]
    [InlineData("127.0.0.1", "old.example", "127.0.0.1")]
    [InlineData("my.server.net", "", "my.server.net")]
    public void OnlyTheRetiredHostMovesToTheCurrentOne(string host, string retired, string expected) =>
        Assert.Equal(expected, ServerHosts.Migrate(host, retired, "new.example"));

    [Theory]
    [InlineData("Siege at nine", 2, 100, "Siege at nine (2/100)")]
    [InlineData("General", 0, 500, "General")]
    public void ThePanelTitleCarriesTheMemberCount(string name, int members, int capacity, string expected) =>
        Assert.Equal(expected, NationChatRules.Title(name, members, capacity));

    [Fact]
    public void TheMemberListPutsTheVoiceCallFirstThenTheRestByName()
    {
        var members = new[] { (1, "zed"), (2, "Amy"), (3, "bob"), (4, "Cal") };
        var inVoice = new HashSet<int> { 4, 1, 9 };

        var arranged = ChatRoomRoster.Arrange(members, inVoice, id => id == 9 ? "Newcomer" : "");

        Assert.Equal([4, 9, 1, 2, 3], arranged.Select(e => e.CharacterId));
        Assert.Equal([true, true, true, false, false], arranged.Select(e => e.InVoice));
        Assert.Equal("Newcomer", arranged[1].Name);
    }

    [Fact]
    public void NormalisingRaisesAQuietNoteButNeverClipsOrBoostsPastTheCap()
    {
        var quiet = new short[] { 0, 1000, -2000, 1500 };
        float gain = VoiceNoteCodec.Normalize(quiet);
        Assert.Equal(VoiceNoteCodec.MaxGain, gain);
        Assert.Equal(-16000, quiet[2]);

        var loud = new short[] { 0, 30000, -32000 };
        Assert.Equal(1f, VoiceNoteCodec.Normalize(loud));
        Assert.Equal(-32000, loud[2]);

        var mid = new short[] { 8000, -10000 };
        VoiceNoteCodec.Normalize(mid);
        Assert.InRange(Math.Abs((int)mid[1]), (int)(VoiceNoteCodec.TargetPeak * short.MaxValue) - 2, (int)(VoiceNoteCodec.TargetPeak * short.MaxValue) + 2);
        Assert.Equal(1f, VoiceNoteCodec.Normalize(new short[4]));
    }

    [Fact]
    public void EnteringARoomRemembersItsNameAndCount()
    {
        var state = new NationChatState();
        state.EnteredPrivate(1002);
        state.Describe("Siege at nine", true, 5, 30);
        state.Counted(6);
        Assert.Equal(("Siege at nine", true, 6, 30), (state.RoomName, state.RoomPrivate, state.RoomMembers, state.RoomMax));
        state.Left();
        Assert.Equal(("", 0), (state.RoomName, state.RoomMembers));
        state.EnteredPublic(1);
        Assert.Equal(NationChatRules.PublicCapacity, state.RoomMax);
    }
}
