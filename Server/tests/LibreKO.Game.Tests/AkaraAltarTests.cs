using FluentAssertions;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AkaraAltarTests
{
    private const int Akara = 31774;
    private const int Karus = 1;

    [Fact]
    public void AkaraOpensHerAltarWhenThePlayerJoins()
    {
        var talk = new QuestTalk(Akara, Karus);

        talk.Labels.Should().Equal("[Akara Altar]");
        talk.Follow("[Akara Altar]");
        talk.Labels.Should().Equal("[Akara Altar] How to Use", "Yes, count me in!");
        talk.Host.DidNotReceive().OpenSpecialAuctionPanel();

        talk.Follow("Yes, count me in!");

        talk.Host.Received(1).OpenSpecialAuctionPanel();
    }

    [Fact]
    public void TheHelpPagesExplainTheAltarWithoutOpeningIt()
    {
        var talk = new QuestTalk(Akara, Karus).Follow("[Akara Altar]", "[Akara Altar] How to Use");

        talk.Labels.Should().Equal("[Akara Altar] Basic Info", "Bidding and Claim", "[Akara Altar] Time");
        talk.Follow("[Akara Altar] Time");
        talk.Header!.Text.Should().StartWith("Akara's Altar Bidding is available 23:05 ~ 22:55 next day.");
        talk.Host.DidNotReceive().OpenSpecialAuctionPanel();
    }

    [Fact]
    public void TheAltarIsDialogStyleFiftyEight()
    {
        var packet = LibreKO.Game.Protocol.Writers.NpcDialogPacketWriter.SpecialAuctionPanel(Akara, "31774_21");
        packet.ResetOffset();
        packet.ReadInt();
        packet.ReadByte().Should().Be(58);
    }
}
