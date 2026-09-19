using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestRequirementTests
{
    private static QuestCompilation Compile(string text)
    {
        var compilation = QuestCompilation.Create(text, "requirements.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return compilation;
    }

    private const string Script = """
        Bind Npc 100 Zone 71
        Quest 1
        Requires player level >= 61 and player is karus
        On topics
            If quest is available
                Topic "Begin" do
                    Start quest
        On accept
            If quest is available
                Start quest
        """;

    [Theory]
    [InlineData(60, 1, 71, 0, false)]
    [InlineData(61, 2, 71, 0, false)]
    [InlineData(61, 1, 72, 0, false)]
    [InlineData(61, 1, 71, 2, false)]
    [InlineData(61, 1, 71, 0, true)]
    public void SharedRequirementsAndTopicConditionsAreCheckedOnReplyAndPanelAccept(
        int level, int nation, int zone, int status, bool allowed)
    {
        var program = Compile(Script).Program;
        var reply = program.Events.Values.Single(e => e.Name is null).Id;
        program.TryGetEntry(QuestProgram.AcceptEvent, 1, out var accept).Should().BeTrue();
        foreach (var entry in new[] { reply, accept })
        {
            var host = Substitute.For<IQuestHost>();
            host.PlayerLevel.Returns(level);
            host.PlayerNation.Returns(nation);
            host.PlayerZone.Returns(zone);
            host.QuestStatus(1).Returns(status);
            new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
            host.Received(allowed ? 1 : 0).SetQuestState(1, 1);
        }
    }

    [Fact]
    public void AcceptedManualQuestAlsoSurvivesAConsumedEntryPrerequisite()
    {
        var program = Compile("""
            Bind Npc 100
            Quest 1
            Requires player has >= 1 of 123
            On fulfil
                If quest is active
                    Complete quest
            """).Program;
        var host = Substitute.For<IQuestHost>();
        host.QuestStatus(1).Returns(1);
        host.ItemCount(123).Returns(0);
        program.TryGetEntry(QuestProgram.FulfilEvent, 1, out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received().SetQuestState(1, 2);
    }

    [Fact]
    public void RequirementsHideOnlyTheirOwnQuestTopicsAfterComposition()
    {
        var other = Compile("""
            Bind Npc 100 Zone 71
            Quest 2
            On topics
                Topic "Other" goto close
            """);
        var program = QuestProgramComposer.Compose("combined", 100, 71, [Compile(Script).Program, other.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(60);
        host.PlayerNation.Returns(1);
        host.PlayerZone.Returns(71);
        program.TryGetGreeting(out var greeting).Should().BeTrue();
        new QuestInterpreter(program, host).Run(greeting);
        host.Received().ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(),
            Arg.Is<IReadOnlyList<DialogButton>>(buttons => buttons.Count == 1 && buttons[0].Label.Text == "Other"));
    }

    [Fact]
    public void AnElseReplyRechecksThatEarlierArmsStillDoNotApply()
    {
        var program = Compile("""
            Bind Npc 100
            Quest 1
            On greeting
                If quest is completed
                    Say "Done"
                Else
                    Say "Ready?"
                    Topic "Yes" do
                        Start quest
            """).Program;
        var reply = program.Events.Values.Single(e => e.Name is null).Id;
        var host = Substitute.For<IQuestHost>();
        host.QuestStatus(1).Returns(2);
        new QuestInterpreter(program, host).Run(reply);
        host.DidNotReceiveWithAnyArgs().SetQuestState(default, default);
    }

    [Fact]
    public void ANewPageDoesNotInheritThePreviousPagesConsumedCondition()
    {
        var program = Compile("""
            Bind Npc 100
            Quest 1
            On greeting
                If quest is available
                    Say "Ready?"
                    Topic "Yes" do
                        Start quest
                        Say "Started"
                        Topic "Continue" do
                            Give 1 coins
            """).Program;
        var host = Substitute.For<IQuestHost>();
        host.QuestStatus(1).Returns(1);
        var next = program.Events.Values.Single(e => e.Name is null && e.Body is [BoundStatement.Action]).Id;
        new QuestInterpreter(program, host).Run(next);
        host.Received().GiveGold(1);
    }

    [Theory]
    [InlineData("Bind Npc 100\nQuest 1\nRequires\n")]
    [InlineData("Bind Npc 100\nQuest 1\nRequires player level >= 2\nRequires player is karus\n")]
    [InlineData("Bind Npc 100\nRequires player level >= 2\n")]
    [InlineData("Bind Npc 100\nQuest 1\nOn accept\n    Start quest\nRequires player level >= 2\n")]
    public void InvalidRequirementDeclarationsAreRejected(string source)
    {
        QuestCompilation.Create(source, "bad.quest").Succeeded.Should().BeFalse();
    }
}
