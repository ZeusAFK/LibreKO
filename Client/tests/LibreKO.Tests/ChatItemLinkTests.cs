using LibreKO.Domain;
using Xunit;

namespace LibreKO.Tests;

public class ChatItemLinkTests
{
    private const int Raptor = 156210008;

    [Fact]
    public void ATokenDecodesToItsItem()
    {
        string token = ChatItemLink.Token(Raptor);
        Assert.StartsWith("<LINK>[", token);
        Assert.EndsWith("]</LINK>", token);
        Assert.Equal(35, token.Length);
        var segments = ChatItemLink.Split("look " + token + " nice");
        Assert.Equal(3, segments.Count);
        Assert.Equal("look ", segments[0].Text);
        Assert.Equal(Raptor, segments[1].ItemId);
        Assert.Equal(" nice", segments[2].Text);
    }

    [Fact]
    public void TheEncodingMatchesTheRetailScrambler() =>
        Assert.Equal("<LINK>[" + ExpectedPayload + "]</LINK>", ChatItemLink.Token(Raptor));

    private const string ExpectedPayload = "OdDGU27pm58eMBWqV1Q/";

    [Fact]
    public void ABrokenLinkIsDroppedAndPlainTextIsKept()
    {
        var segments = ChatItemLink.Split("a <LINK>[!!]</LINK> b <LINK>[unclosed");
        Assert.Single(segments);
        Assert.Equal("a  b <LINK>[unclosed", segments[0].Text);
        Assert.Equal(0, segments[0].ItemId);
    }

    [Fact]
    public void TheDraftShowsTheNameAndSendsTheToken()
    {
        var draft = new ChatLinkDraft();
        var (text, caret) = draft.Insert("see ", 4, Raptor, "Raptor(+8)");
        Assert.Equal("see [Raptor(+8)]", text);
        Assert.Equal(text.Length, caret);
        Assert.Equal("see " + ChatItemLink.Token(Raptor) + "!", draft.ToWire(text + "!"));
        Assert.Equal(ChatItemLink.Token(Raptor).Length - "[Raptor(+8)]".Length, draft.WireExtra);
    }

    [Fact]
    public void OnlyOneLinkFitsAMessage()
    {
        var draft = new ChatLinkDraft();
        var (text, _) = draft.Insert("", 0, Raptor, "Raptor(+8)");
        var (again, _) = draft.Insert(text, text.Length, 123, "Other");
        Assert.Equal(text, again);
    }

    [Fact]
    public void TypingBeforeTheLinkMovesItAndTypingInsideRemovesIt()
    {
        var draft = new ChatLinkDraft();
        var (text, _) = draft.Insert("see ", 4, Raptor, "Raptor(+8)");
        draft.TextChanged(text, "I " + text);
        Assert.Equal("I see " + ChatItemLink.Token(Raptor), draft.ToWire("I " + text));

        draft.TextChanged("I " + text, "I see [Raptr(+8)]");
        Assert.False(draft.Active);
        Assert.Equal("I see [Raptr(+8)]", draft.ToWire("I see [Raptr(+8)]"));
    }
}
