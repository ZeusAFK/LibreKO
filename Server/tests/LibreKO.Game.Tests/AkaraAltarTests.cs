using FluentAssertions;
using LibreKO.Game.World;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AkaraAltarTests
{
    private const int Akara = 31774;
    private const int Karus = 1;
    private const string LoveKnightOnline = "I Love Knight online";
    private const string ContinuePlaying = "I will continue playing knight online";
    private const string EmblemExchange = "[Exchange] I love knight online Emblem Voucher";
    private const string JustGo = "I'll just go.";
    private const int Emblem = 1113329000;
    private const int DecorativeEmblem = 1113330000;
    private const int HoursPerDay = 24;

    [Fact]
    public void AkaraOpensHerAltarWhenThePlayerJoins()
    {
        var talk = new QuestTalk(Akara, Karus);

        talk.Labels.Should().Equal("[Akara Altar]", LoveKnightOnline, ContinuePlaying, EmblemExchange);
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

    [Theory]
    [InlineData(LoveKnightOnline, 0)]
    [InlineData(ContinuePlaying, 1)]
    public void EachEventTopicOpensItsPostBoard(string topic, int eventId)
    {
        var talk = new QuestTalk(Akara, Karus).Follow(topic);

        talk.Host.Received(1).OpenPostBoard(eventId);
        talk.Host.DidNotReceive().OpenSpecialAuctionPanel();
    }

    [Theory]
    [InlineData(0, 69)]
    [InlineData(1, 72)]
    public void ThePostBoardsAreDialogStylesSixtyNineAndSeventyTwo(int eventId, byte style)
    {
        var packet = LibreKO.Game.Protocol.Writers.NpcDialogPacketWriter.PostBoard(Akara, "31774_21", eventId)!;
        packet.ResetOffset();
        packet.ReadInt();
        packet.ReadByte().Should().Be(style);
    }

    [Theory]
    [InlineData(EventPostRules.DailyVoucher, "Emblem Exchange Voucher (1day)", Emblem, 1)]
    [InlineData(EventPostRules.ThreeDayVoucher, "Emblem Exchange Voucher (3days)", Emblem, 3)]
    [InlineData(EventPostRules.SevenDayVoucher, "Emblem Exchange Voucher (7days)", Emblem, 7)]
    [InlineData(EventPostRules.FifteenDayVoucher, "Emblem Exchange Voucher (15days)", Emblem, 15)]
    [InlineData(EventPostRules.ThirtyDayVoucher, "Emblem Exchange Voucher (30days)", Emblem, 30)]
    [InlineData(EventPostRules.DecorativeVoucher, "Decorative Emblem Exchange Voucher", DecorativeEmblem, 0)]
    public void EachVoucherTradesForTheEmblemForItsDays(int voucher, string topic, int emblem, int days)
    {
        var talk = new QuestTalk(Akara, Karus, voucher).Follow(EmblemExchange);
        talk.Labels.Should().Equal(topic, JustGo);

        talk.Follow(topic);

        talk.Host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 2
            && actions.Any(action => action.Kind == QuestActionKind.TakeItem && action.Arguments.GetInt("item") == voucher
                && action.Arguments.GetInt("count", 1) == 1)
            && actions.Any(action => action.Kind == QuestActionKind.GiveItem && action.Arguments.GetInt("item") == emblem
                && action.Arguments.GetInt("count", 1) == 1 && QuestInterpreter.RentalHours(action.Arguments) == days * HoursPerDay)));
    }

    [Fact]
    public void EveryVoucherHeldIsOffered()
    {
        var talk = new QuestTalk(Akara, Karus, EventPostRules.DailyVoucher, EventPostRules.ThirtyDayVoucher, EventPostRules.DecorativeVoucher)
            .Follow(EmblemExchange);

        talk.Labels.Should().Equal("Emblem Exchange Voucher (1day)", "Emblem Exchange Voucher (30days)",
            "Decorative Emblem Exchange Voucher", JustGo);
    }

    [Fact]
    public void WithoutAVoucherAkaraSaysHowToEarnOne()
    {
        var talk = new QuestTalk(Akara, Karus).Follow(EmblemExchange);

        talk.Labels.Should().Equal("Confirm");
        talk.Header!.Text.Should().StartWith("You don't have an I love knight online Emblem Exchange Voucher.");
        talk.Host.DidNotReceive().ApplyReward(Arg.Any<IReadOnlyList<BoundStatement.Action>>());
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
