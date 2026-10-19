using LibreKO.Domain;
using LibreKO.Network;
using Xunit;

namespace LibreKO.Tests;

public class ChatReactionsTests
{
    private static readonly DateTime Noon = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly uint NoonUnix = (uint)new DateTimeOffset(Noon).ToUnixTimeSeconds();
    private const string Heart = "❤️";

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

    private static void WriteReaction(Packet p, long id, int who, string name, string emoji)
    {
        p.WriteLong(id);
        p.WriteInt(who);
        p.WriteString(name);
        p.WriteString(emoji);
    }

    [Fact]
    public void TheTallyCountsEachEmojiAndOrdersByCountThenFirstSeen()
    {
        var reactions = new MessageReactions();
        reactions.Set(1, "Rikka", "🙏");
        reactions.Set(2, "Borin", Heart);
        reactions.Set(3, "Arwen", Heart);
        reactions.Set(4, "Kaito", "😂");

        Assert.Equal(
            [new ReactionCount(Heart, 2), new ReactionCount("🙏", 1), new ReactionCount("😂", 1)],
            reactions.Tally());
        Assert.Equal(4, reactions.Total);
        Assert.Equal("4", ReactionRules.Count(reactions.Total));
        Assert.Equal("", ReactionRules.Count(1));
        Assert.Equal("4 reactions", ReactionRules.Title(4));
        Assert.Equal("1 reaction", ReactionRules.Title(1));
    }

    [Fact]
    public void EachPersonHoldsOneReactionThatCanBeReplacedOrRemoved()
    {
        var reactions = new MessageReactions();

        Assert.True(reactions.Set(7, "Rikka", "👍"));
        Assert.False(reactions.Set(7, "Rikka", "👍"));
        Assert.True(reactions.Set(7, "Rikka", Heart));
        Assert.Equal(Heart, reactions.Of(7));
        Assert.Equal(1, reactions.Total);
        Assert.True(reactions.Set(7, "Rikka", ""));
        Assert.False(reactions.Set(7, "Rikka", ""));
        Assert.Equal("", reactions.Of(7));
        Assert.Equal(0, reactions.Total);
    }

    [Fact]
    public void PickingYourOwnReactionAgainRemovesIt()
    {
        Assert.Equal("", ReactionRules.Toggle(Heart, Heart));
        Assert.Equal("😂", ReactionRules.Toggle(Heart, "😂"));
        Assert.Equal("👍", ReactionRules.Toggle("", "👍"));
        Assert.True(ReactionRules.Valid("(Smile)"));
        Assert.False(ReactionRules.Valid(""));
        Assert.False(ReactionRules.Valid("a\nb"));
        Assert.False(ReactionRules.Valid(new string('x', ReactionRules.MaxLength + 1)));
    }

    [Fact]
    public void TheDetailsListPutsYouFirstAndFiltersByEmoji()
    {
        var reactions = new MessageReactions();
        reactions.Set(3, "Zed", Heart);
        reactions.Set(9, "Me", "🙏");
        reactions.Set(2, "Arwen", Heart);

        Assert.Equal([9, 2, 3], reactions.Listed("", self: 9).Select(r => r.CharacterId));
        Assert.Equal([2, 3], reactions.Listed(Heart, self: 9).Select(r => r.CharacterId));
    }

    [Fact]
    public void AStoredRowTagsTheLiveLineItBelongsTo()
    {
        var timeline = new RoomTimeline();
        timeline.MergeHistory([], more: false);
        var first = RoomPost.Line(5, "Rikka", "one", Noon);
        var second = RoomPost.Line(5, "Rikka", "two", Noon);
        timeline.Add(first);
        timeline.Add(second);

        var (tagged, added) = timeline.Stored(new RoomPost(90, RoomPostKind.Text, 5, "Rikka", Noon, "two", 0, 0));

        Assert.False(added);
        Assert.Same(second, tagged);
        Assert.Equal(90, second.Id);
        Assert.Equal(0, first.Id);
        Assert.Same(second, timeline.Find(90));
        Assert.Equal(2, timeline.Posts.Count);
        Assert.Same(second, timeline.Stored(new RoomPost(90, RoomPostKind.Text, 5, "Rikka", Noon, "two", 0, 0)).Post);
    }

    [Fact]
    public void AStoredRowNoLineShowedIsAddedAndHistoryDoesNotRepeatIt()
    {
        var timeline = new RoomTimeline();
        var early = RoomPost.Line(5, "Rikka", "hello", Noon);
        timeline.Add(early);
        timeline.Stored(new RoomPost(70, RoomPostKind.Text, 5, "Rikka", Noon, "hello", 0, 0));

        var (note, added) = timeline.Stored(new RoomPost(71, RoomPostKind.VoiceNote, 6, "Borin", Noon, "", 30, 900));
        timeline.MergeHistory(
        [
            new RoomPost(69, RoomPostKind.Text, 6, "Borin", Noon, "earlier", 0, 0),
            new RoomPost(70, RoomPostKind.Text, 5, "Rikka", Noon, "hello", 0, 0),
        ], more: false);

        Assert.True(added);
        Assert.Equal(RoomPostKind.VoiceNote, note.Kind);
        Assert.Equal([69, 70, 71], timeline.Posts.Select(p => p.Id));
        Assert.Same(early, timeline.Find(70));
    }

    [Fact]
    public void ReactionPacketsReadTheServerLayout()
    {
        var one = Media(RoomMediaSubOpcode.React);
        WriteReaction(one, 41, 7, "Rikka", Heart);
        Assert.Equal(new RoomMediaReplies.ReactionChange(41, 7, "Rikka", Heart), RoomMediaReplies.ReadReaction(Ready(one)));

        var list = Media(RoomMediaSubOpcode.Reactions);
        list.WriteUShort(2);
        WriteReaction(list, 41, 7, "Rikka", Heart);
        WriteReaction(list, 42, 8, "Borin", "");
        var ready = Ready(list);
        var read = RoomMediaReplies.ReadReactions(ready);
        Assert.Equal([new(41, 7, "Rikka", Heart), new RoomMediaReplies.ReactionChange(42, 8, "Borin", "")], read);
        Assert.Equal(0, ready.RemainingBytes);

        var kept = Media(RoomMediaSubOpcode.Kept);
        kept.WriteByte((byte)RoomMediaResult.Ok);
        kept.WriteByte(1);
        Assert.Equal(((byte)RoomMediaResult.Ok, true), RoomMediaReplies.ReadKept(Ready(kept)));
    }

    [Fact]
    public void WhisperRowsAndPagesCarryThePartnerName()
    {
        var stored = Media(RoomMediaSubOpcode.DirectStored);
        stored.WriteString("Rikka");
        WriteTextRow(stored, 55, 7, "Rikka", "psst");
        var direct = RoomMediaReplies.ReadDirectRow(Ready(stored));
        Assert.NotNull(direct);
        Assert.Equal("Rikka", direct.Value.Partner);
        Assert.Equal(55, direct.Value.Row.Id);
        Assert.Equal("psst", direct.Value.Row.Text);

        var page = Media(RoomMediaSubOpcode.DirectHistory);
        page.WriteString("Rikka");
        page.WriteByte(1);
        WriteTextRow(page, 54, 9, "Tester10", "hi");
        page.WriteByte(1);
        var (partner, rows, more) = RoomMediaReplies.ReadDirectHistory(Ready(page));
        Assert.Equal("Rikka", partner);
        Assert.Single(rows);
        Assert.Equal("hi", rows[0].Text);
        Assert.True(more);
    }

    [Fact]
    public void AKeptRoomFlagClearsWhenTheRoomIsLeft()
    {
        var state = new NationChatState();
        state.KeptChanged(true);
        Assert.False(state.Kept);

        state.EnteredPrivate(1001);
        state.KeptChanged(true);
        Assert.True(state.Kept);

        state.Left();
        Assert.False(state.Kept);
    }
}
