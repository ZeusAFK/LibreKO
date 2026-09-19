using FluentAssertions;
using NSubstitute;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;

namespace LibreKO.Game.Tests;

public class QuestRewardTableTests
{
    private const string Pool = """
        RewardPool simple_drop
            810418000 weight 100
            900044000 weight 50
            379156000

        Bind Npc 100
        Quest 61 "Pool"

        On fulfil
            Transaction
                Give random from simple_drop
        """;

    private const string Table = """
        RewardTable chest
            Weights 1500 1500 2000 2500 2500

            Row 121210003 205002003 206002003 121310003 160450255
            Row 126310003 205003003 206003003 126410003 169101104
                Weights 2300 2599 2600 2500 1

        Bind Npc 100
        Quest 61 "Table"

        On fulfil
            Transaction
                Take 1 of 379156000
                Give random from chest
        """;

    private static (QuestProgram Program, IQuestHost Host, int Entry) Ready(string source, params int[] rolls)
    {
        var result = QuestCompilation.Create(source, "rewards.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        var queue = new Queue<int>(rolls);
        host.RollDice(Arg.Any<int>()).Returns(_ => queue.Count > 0 ? queue.Dequeue() : 0);
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var entry).Should().BeTrue();
        return (result.Program, host, entry);
    }

    private static IReadOnlyList<BoundStatement.Action> Applied(IQuestHost host)
    {
        var call = host.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IQuestHost.ApplyReward));
        return (IReadOnlyList<BoundStatement.Action>)call.GetArguments()[0]!;
    }

    [Theory]
    [InlineData(0, 810418000)]
    [InlineData(99, 810418000)]
    [InlineData(100, 900044000)]
    [InlineData(149, 900044000)]
    [InlineData(150, 379156000)]
    public void APoolPicksByWeightAcrossTheWholeRange(int roll, int expected)
    {
        var (program, host, entry) = Ready(Pool, roll);
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        var applied = Applied(host);
        applied.Should().ContainSingle();
        applied[0].Kind.Should().Be(QuestActionKind.GiveItem);
        applied[0].Arguments.GetInt("item").Should().Be(expected);
    }

    [Fact]
    public void APoolRollsOverTheSumOfItsWeights()
    {
        var (program, host, entry) = Ready(Pool, 0);
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received().RollDice(150);
    }

    [Theory]
    [InlineData(0, 0, 121210003)]
    [InlineData(0, 1500, 205002003)]
    [InlineData(0, 9999, 160450255)]
    [InlineData(1, 0, 126310003)]
    [InlineData(1, 2300, 205003003)]
    [InlineData(1, 9999, 169101104)]
    public void ATablePicksARowThenAnItemUsingThatRowsWeights(int row, int within, int expected)
    {
        var (program, host, entry) = Ready(Table, row, within);
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        var applied = Applied(host);
        applied.Should().HaveCount(2);
        applied[0].Kind.Should().Be(QuestActionKind.TakeItem);
        applied[1].Arguments.GetInt("item").Should().Be(expected);
    }

    [Fact]
    public void ARowOverridesTheSharedWeightsOnlyForItself()
    {
        var (program, host, entry) = Ready(Table, 1, 0);
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received().RollDice(1);
        host.Received().RollDice(9999);
    }

    [Theory]
    [InlineData("RewardPool p\n    0 810418000\n\nBind Npc 100\nQuest 61 \"x\"\nOn fulfil\n    Transaction\n        Give random from p")]
    [InlineData("RewardTable t\n    Weights 1 2\n    Row 810418000\n\nBind Npc 100\nQuest 61 \"x\"\nOn fulfil\n    Transaction\n        Give random from t")]
    [InlineData("Bind Npc 100\nQuest 61 \"x\"\nOn fulfil\n    Transaction\n        Give random from missing")]
    public void ARewardDefinitionThatCannotGiveAnythingIsRejected(string source)
    {
        QuestCompilation.Create(source, "rewards.quest").Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("Trade 2 of 810418000 for 3 of 379156000", 2, 3)]
    [InlineData("Trade 1 of 810418000 for 1 of 379156000", 1, 1)]
    public void TradeIsOneTakeAndOneGiveInASingleTransaction(string line, int taken, int given)
    {
        var result = QuestCompilation.Create($"""
            Bind Npc 100
            Quest 61 "Trade"

            On fulfil
                {line}
            """, "trade.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var entry).Should().BeTrue();
        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();
        var applied = Applied(host);
        applied.Should().HaveCount(2);
        applied[0].Kind.Should().Be(QuestActionKind.TakeItem);
        applied[0].Arguments.GetInt("count").Should().Be(taken);
        applied[1].Kind.Should().Be(QuestActionKind.GiveItem);
        applied[1].Arguments.GetInt("count").Should().Be(given);
    }

    [Fact]
    public void TradeCanHandOverARandomReward()
    {
        var result = QuestCompilation.Create("""
            RewardPool box
                810418000

            Bind Npc 100
            Quest 61 "Trade"

            On fulfil
                Trade 1 of 379156000 for random from box
            """, "trade.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var entry).Should().BeTrue();
        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();
        var applied = Applied(host);
        applied.Should().HaveCount(2);
        applied[1].Arguments.GetInt("item").Should().Be(810418000);
    }

    [Theory]
    [InlineData("Trade 1 of 123")]
    [InlineData("Trade for 1 of 123")]
    public void TradeNeedsBothSides(string line)
    {
        QuestCompilation.Create($"Bind Npc 100\nQuest 61 \"t\"\nOn fulfil\n    {line}",
            "trade.quest").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void APoolEntryCarriesItsOwnQuantity()
    {
        var result = QuestCompilation.Create("""
            RewardPool stack
                3 of 810418000

            Bind Npc 100
            Quest 61 "Pool"

            On fulfil
                Transaction
                    Give random from stack
            """, "rewards.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var entry).Should().BeTrue();
        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();
        Applied(host)[0].Arguments.GetInt("count").Should().Be(3);
    }

    [Fact]
    public void ThePoolSpellingThatNeverShippedIsNotAccepted()
    {
        QuestCompilation.Create("""
            RewardPool old
                100 810418000

            Bind Npc 100
            Quest 61 "Pool"

            On fulfil
                Transaction
                    Give random from old
            """, "rewards.quest").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void ARewardDefinitionCanComeFromAnInclude()
    {
        var includes = new DictionaryIncludes(new()
        {
            ["rewards/shrine"] = "RewardPool shrine_drop\n    810418000 weight 10\n"
        });
        var result = QuestCompilation.Create("""
            include rewards/shrine

            Bind Npc 100
            Quest 61 "Included"

            On fulfil
                Transaction
                    Give random from shrine_drop
            """, "rewards.quest", includes: includes);
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var entry).Should().BeTrue();
        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();
        Applied(host)[0].Arguments.GetInt("item").Should().Be(810418000);
    }

    private sealed class DictionaryIncludes(Dictionary<string, string> files) : IQuestIncludes
    {
        public bool TryRead(string path, out string text, out string fileName)
        {
            fileName = path;
            return files.TryGetValue(path, out text!);
        }
    }
}
