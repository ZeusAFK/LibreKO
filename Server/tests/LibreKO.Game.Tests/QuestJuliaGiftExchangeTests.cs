using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestJuliaGiftExchangeTests
{
    private const int Julia = 31741;
    private const int Moradon = 21;
    private const int DelicateGift = 931685000;
    private const int ShiningGift = 931684000;
    private const int GloriousGift = 931683000;
    private const string ExchangeTopic = "Exchange Dason's Gift";
    private const string WrongItemLine = "I'm afraid that is not what I've asked for";

    private static readonly int[] GiftPool =
    [
        810656000, 810657000, 810658000, 810659000, 810660000, 810661000, 810662000, 810663000,
        379109000, 379129000, 389093000, 810201000,
        399127000, 399128000, 399129000,
        910202000, 910203000, 910204000,
    ];

    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private sealed class Talk
    {
        public readonly QuestProgram Program;
        public readonly IQuestHost Host;
        public IReadOnlyList<DialogButton> Shown = [];
        public DialogLine? Header;

        public Talk(int boxInBag)
        {
            var compilation = QuestCompilation.CreateFromFile(BakedQuestPath($"{Julia}.quest"));
            compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
            Program = QuestProgramComposer.Compose("julia", Julia, Moradon, [compilation.Program]);
            Host = Substitute.For<IQuestHost>();
            Host.PlayerZone.Returns(Moradon);
            Host.PlayerLevel.Returns(83);
            Host.ItemCount(boxInBag).Returns(1);
            Host.HasRoomForItem(Arg.Any<int>(), Arg.Any<int>()).Returns(true);
            Host.CanReceiveStacks(Arg.Any<int>()).Returns(true);
            Host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
                .Do(c =>
                {
                    Header = c.ArgAt<DialogLine>(2);
                    Shown = c.ArgAt<IReadOnlyList<DialogButton>>(3);
                });
        }

        public void Greet()
        {
            Program.TryGetGreeting(out var greeting).Should().BeTrue();
            new QuestInterpreter(Program, Host).Run(greeting).Failure.Should().BeNull();
        }

        public void Follow(string label)
        {
            var button = Shown.Single(b => b.Label.Text == label);
            new QuestInterpreter(Program, Host).Run(button.TargetEvent).Failure.Should().BeNull();
        }
    }

    [Fact]
    public void TheGiftMenuOffersTheThreeBoxes()
    {
        var talk = new Talk(DelicateGift);
        talk.Greet();
        talk.Follow(ExchangeTopic);

        talk.Header!.Text.Should().Be("Please select one of the following options");
        talk.Shown.Select(b => b.Label.Text).Should().Equal("Dason's Delicate Gift", "Dason's Shining Gift", "Dason's Glorious Gift", "Close");
    }

    [Theory]
    [InlineData(DelicateGift, "Dason's Delicate Gift")]
    [InlineData(ShiningGift, "Dason's Shining Gift")]
    [InlineData(GloriousGift, "Dason's Glorious Gift")]
    public void ABoxIsTradedForOneDrawFromItsPool(int box, string topic)
    {
        var talk = new Talk(box);
        talk.Greet();
        talk.Follow(ExchangeTopic);
        talk.Follow(topic);

        talk.Host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Count == 2
            && actions.Any(a => a.Kind == QuestActionKind.TakeItem && a.Arguments.GetInt("item") == box && a.Arguments.GetInt("count", 1) == 1)
            && actions.Any(a => a.Kind == QuestActionKind.GiveItem && GiftPool.Contains(a.Arguments.GetInt("item")) && a.Arguments.GetInt("count", 1) == 1)));
    }

    [Fact]
    public void ARefusedTradeAnswersWithJuliasLine()
    {
        var talk = new Talk(DelicateGift);
        talk.Host.When(h => h.ApplyReward(Arg.Any<IReadOnlyList<BoundStatement.Action>>())).Do(_ => talk.Host.ActionFailed.Returns(true));
        talk.Greet();
        talk.Follow(ExchangeTopic);
        talk.Follow("Dason's Shining Gift");

        talk.Host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<BoundStatement.Action>>(actions =>
            actions.Any(a => a.Kind == QuestActionKind.TakeItem && a.Arguments.GetInt("item") == ShiningGift)));
        talk.Header!.Text.Should().Be(WrongItemLine);
    }
}
