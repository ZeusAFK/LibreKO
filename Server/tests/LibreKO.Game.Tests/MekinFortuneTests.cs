using FluentAssertions;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MekinFortuneTests
{
    private const int Mekin = 19005;
    private const int ElMorad = 2;
    private const int Fee = 10_000;
    private const string FortuneFile = "19005_21_fortune.quest";
    private const string Offer = "Okay, great. Shall we begin?";
    private const string Short = "Yeah, well, we could start, but you don't seem to have the 10,000 Noah...";

    private static readonly string[] MekinFiles =
    [
        "19005_21.quest", "19005_21_1119.quest", "19005_21_1120.quest", "19005_21_1121.quest", "19005_21_1122.quest",
        "19005_21_1720.quest", "19005_21_1722.quest", "19005_21_1723.quest", FortuneFile,
    ];

    private static QuestTalk Fortune(int coins) =>
        new(FortuneFile, "fortune", Mekin, ElMorad, host => host.PlayerCoins.Returns(coins));

    private static QuestTalk Greeting(Action<IQuestHost> arrange) => new(MekinFiles, null, Mekin, ElMorad, arrange);

    [Fact]
    public void ACharacterWithNoRebirthStepStillHearsTheFortune()
    {
        var talk = Greeting(host => host.PlayerCoins.Returns(Fee));

        talk.Header!.Text.Should().Be("You don't have enough EXP. You have be to be level 83 with 100 % EXP.");
        talk.Labels.Should().Equal("Keep talking", "Close");

        talk.Follow("Keep talking");

        talk.Header!.Text.Should().Be(Offer);
    }

    [Fact]
    public void ACharacterReadyForRebirthSeesBothWithoutARepeat()
    {
        var talk = Greeting(host =>
        {
            host.PlayerLevel.Returns(83);
            host.ReachedLevel(83, 100).Returns(true);
        });

        talk.Labels.Should().Contain("Keep talking").And.HaveCountGreaterThan(1);
        talk.Labels.Count(label => label == "Keep talking").Should().Be(1);
    }

    [Fact]
    public void MekinReadsTheCardsForTenThousandCoins()
    {
        var talk = Fortune(Fee);

        talk.Header!.Text.Should().Be(Offer);
        talk.Labels.Should().Equal("Give 10,000 Noah", "Back");

        talk.Follow("Give 10,000 Noah");

        talk.Host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 1 && actions[0].Kind == QuestActionKind.TakeGold && actions[0].Arguments.GetInt("amount") == Fee));
        talk.Host.Received(1).OpenFortunePanel();
    }

    [Fact]
    public void WithoutTheFeeMekinSaysSo()
    {
        var talk = Fortune(Fee - 1);

        talk.Header!.Text.Should().Be(Short);
        talk.Host.DidNotReceive().OpenFortunePanel();
    }

    [Fact]
    public void TheFortuneWindowIsDialogStyleSixteen()
    {
        var packet = LibreKO.Game.Protocol.Writers.NpcDialogPacketWriter.FortunePanel(Mekin, "19005_21_fortune");
        packet.ResetOffset();
        packet.ReadInt();
        packet.ReadByte().Should().Be(16);
    }
}
