using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Catalog;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestObjectiveTests
{
    private static QuestCompilation Compile(string text) =>
        QuestCompilation.Create(text, "objectives.quest");

    private sealed class OneQuestPerNpc : IQuestCatalog
    {
        public bool KnowsItems => false;
        public bool KnowsExchanges => false;
        public bool KnowsQuests => true;
        public bool KnowsNpcs => false;
        public bool KnowsZones => false;
        public bool KnowsText => false;

        public string? ItemName(long itemId) => null;
        public bool ItemStacks(long itemId) => true;
        public string? ExchangeSummary(long exchangeId) => null;
        public string? QuestSummary(long questId) => null;
        public string? QuestTitle(long questId) => questId == 111 ? "Orc Watcher hunting" : "Elsewhere";
        public IReadOnlyCollection<long> NpcsForQuest(long questId) => questId == 111 ? [13013L] : [99999L];
        public string? EventTriggerSummary(long npcId, long eventId) => null;
        public string? NpcName(long npcId) => null;
        public string? ZoneName(long zoneId) => null;
        public string? MapName(long mapId) => null;
        public string? TalkText(long textId) => null;
        public string? MenuText(long textId) => null;
    }

    private const string Hunt = """
        Bind Npc 13013

        orc_watcher = npc 1671

        Quest 111
            Kill 10 of orc_watcher

        On greeting
            Say "What can I do for you?"
            Topic "Nothing" goto close
        """;

    [Fact]
    public void AQuestBlockDeclaresWhatHasToDie()
    {
        var compilation = Compile(Hunt);

        compilation.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var objectives = compilation.Program.Objectives.Should().ContainSingle().Subject;
        objectives.QuestId.Should().Be(111);
        objectives.Groups.Should().ContainSingle();
        objectives.Groups[0].Count.Should().Be(10);
        objectives.Groups[0].Monsters.Should().Equal(1671);
    }

    [Fact]
    public void SeveralMonstersOnOneLineCountTowardTheSameObjective()
    {
        var program = Compile("""
            Bind Npc 13013

            Quest 111
                Kill 10 of 700, 750, 751, 752

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Program;

        program.Objectives[0].Groups.Should().ContainSingle();
        program.Objectives[0].Groups[0].Monsters.Should().Equal(700, 750, 751, 752);
    }

    [Theory]
    [InlineData("Quest 32768\n    Kill 1 of 100")]
    [InlineData("Quest 777\n    Kill 32768 of 100")]
    [InlineData("Quest 777\n    Kill 1 of 32768")]
    [InlineData("Quest 777\n    Kill 1 of 4294967396")]
    [InlineData("Quest 777\n    Kill 1 of 100, 101, 102, 103, 104")]
    public void ObjectivesThatCannotBeTrackedOrSentAreRejected(string objectives)
    {
        Compile("Bind Npc 100\n" + objectives + "\nOn greeting\n    Give 1 coins\n").Succeeded
            .Should().BeFalse();
    }

    [Fact]
    public void EachKillLineIsItsOwnObjective()
    {
        var program = Compile("""
            Bind Npc 13013

            Quest 111
                Kill 10 of 1671
                Kill 5 of 1672

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Program;

        program.Objectives[0].Groups.Should().HaveCount(2);
        program.Objectives[0].Groups[1].Count.Should().Be(5);
    }

    [Fact]
    public void AQuestTracksFourObjectivesAtMost()
    {
        Compile("""
            Bind Npc 13013

            Quest 111
                Kill 1 of 1671
                Kill 1 of 1672
                Kill 1 of 1673
                Kill 1 of 1674
                Kill 1 of 1675

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.TooManyKillGroups);
    }

    [Fact]
    public void AQuestListsItsObjectivesOnlyOnce()
    {
        Compile("""
            Bind Npc 13013

            Quest 111
                Kill 10 of 1671

            Quest 111
                Kill 5 of 1672

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.DuplicateObjectives);
    }

    [Fact]
    public void AnObjectiveHasToAskForAKill()
    {
        Compile("""
            Bind Npc 13013

            Quest 111
                Kill 0 of 1671

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.BadArgumentCount);
    }

    [Fact]
    public void AQuestBlockTakesNothingButKillLines()
    {
        Compile("""
            Bind Npc 13013

            Quest 111
                Say "not here"

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.UnknownAction);
    }

    [Fact]
    public void QuestDataMayBeCarriedByAnNpcOtherThanThePanelOwner()
    {
        QuestCompilation.Create(
                Hunt.Replace("Quest 111", "Quest 78"), "objectives.quest", new OneQuestPerNpc())
            .Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.QuestNotAtNpc);
    }

    [Fact]
    public void AQuestNeedsEveryObjectiveUnlessItSaysOtherwise()
    {
        Compile(Hunt).Program.Objectives[0].Rule.Should().Be(ObjectiveRule.All);
    }

    [Fact]
    public void AQuestCanSayOneObjectiveIsEnough()
    {
        var program = Compile("""
            Bind Npc 13013

            Quest 111 needs any
                Kill 10 of 1671
                Kill 5 of 1672

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Program;

        program.Objectives[0].Rule.Should().Be(ObjectiveRule.Any);
    }

    [Fact]
    public void NeedsAnySaysNothingAboutASingleObjective()
    {
        Compile("""
            Bind Npc 13013

            Quest 111 needs any
                Kill 10 of 1671

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.UnusedQuestScope);
    }

    [Fact]
    public void OnlyNeedsAnyOrNeedsAllFollowTheQuestId()
    {
        Compile("""
            Bind Npc 13013

            Quest 111 sometimes
                Kill 10 of 1671

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics
            .Should().Contain(d => d.Id == DiagnosticId.UnexpectedToken);
    }

    [Fact]
    public void AConditionCanAskAboutEveryObjectiveAtOnce()
    {
        var compilation = Compile("""
            Bind Npc 13013

            Quest 111
                Kill 10 of 1671

            On greeting
                Say "Well?"
                If player kills < 10 in all of quest 111
                    Topic "Not yet" goto close
                Topic "Nothing" goto close
            """);

        compilation.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(ObjectiveRule.All, 10, 0, 0, false)]
    [InlineData(ObjectiveRule.All, 10, 10, 0, true)]
    [InlineData(ObjectiveRule.All, 10, 10, 5, false)]
    [InlineData(ObjectiveRule.Any, 10, 10, 5, true)]
    [InlineData(ObjectiveRule.Any, 10, 0, 5, false)]
    [InlineData(ObjectiveRule.Any, 10, 5, 0, false)]
    public void OneObjectiveFinishesAnAnyQuestAndEveryOneFinishesAnAllQuest(
        ObjectiveRule rule, short firstRequired, ushort firstDone, short secondRequired, bool expected)
    {
        short[] required = [firstRequired, secondRequired, 0, 0];
        ushort[] done = [firstDone, 0, 0, 0];

        QuestProgressionService
            .AreKillObjectivesComplete(required, done, rule)
            .Should().Be(expected);
    }

    private static QuestProgressionService ServiceWith(IQuestDefinitionSource source, IGameDataService data) =>
        new(null!, data, Substitute.For<IQuestDialogRunner>(), source,
            Substitute.For<ICharacterStatePersister>(),
            Substitute.For<ILogger<QuestProgressionService>>());

    [Fact]
    public void TheScriptsObjectivesAreUsed()
    {
        var source = Substitute.For<IQuestDefinitionSource>();
        source.ObjectivesFor(111).Returns(new QuestObjectives(
            111, [new KillObjective(7, [1671, 1672])]));

        var data = Substitute.For<IGameDataService>();

        ServiceWith(source, data).TryGetKillObjectives(111, out var groups, out var counts)
            .Should().BeTrue();

        counts[0].Should().Be(7, "the script declares 7");
        groups[0].Should().Equal((short)1671, (short)1672);
    }

    [Fact]
    public void AQuestWithoutDeclaredObjectivesIsNotAKillQuest()
    {
        var source = Substitute.For<IQuestDefinitionSource>();
        source.ObjectivesFor(111).Returns((QuestObjectives?)null);
        var data = Substitute.For<IGameDataService>();

        ServiceWith(source, data).TryGetKillObjectives(111, out _, out _)
            .Should().BeFalse();
    }
}
