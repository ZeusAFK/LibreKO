using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Catalog;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestTextTests
{
    private sealed class StaleTitles : IQuestCatalog
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
        public string? QuestTitle(long questId) => "what the table used to say";
        public IReadOnlyCollection<long> NpcsForQuest(long questId) => [];
        public string? EventTriggerSummary(long npcId, long eventId) => null;
        public string? NpcName(long npcId) => null;
        public string? ZoneName(long zoneId) => null;
        public string? MapName(long mapId) => null;
        public string? TalkText(long textId) => null;
        public string? MenuText(long textId) => null;
    }

    private static QuestCompilation Compile(string text, IQuestCatalog? catalog = null) =>
        QuestCompilation.Create(text, "strings.quest", catalog);

    private const string Delivery = """
        Bind Npc 16079

        Quest 61 "Silk Spool"
            Journal "Menissiah wants 2 Apple of Moradon."

        On greeting
            Say "Hello."
            Topic "Nothing" goto close
        """;

    [Fact]
    public void AQuestBlockCarriesItsTitleAndJournal()
    {
        var compilation = Compile(Delivery);

        compilation.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        var text = compilation.Program.Texts.Should().ContainSingle().Subject;
        text.QuestId.Should().Be(61);
        text.Title.Should().Be("Silk Spool");
        text.Journal.Should().Be("Menissiah wants 2 Apple of Moradon.");
    }

    [Fact]
    public void ADeliveryQuestDeclaresNoKills()
    {
        Compile(Delivery).Program.Objectives.Should().BeEmpty();
    }

    [Fact]
    public void AQuestBlockThatSaysNothingIsStillEmpty()
    {
        Compile("""
            Quest 61

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics.Should().Contain(d => d.Id == DiagnosticId.EmptyBlock);
    }

    [Fact]
    public void ABoundFileMayDeclareOnlyTheQuestIdAndTakeItsTextFromTheCatalog()
    {
        Compile("""
            Bind Npc 16079

            Quest 61

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics.Should().NotContain(d => d.Id == DiagnosticId.EmptyBlock);
    }

    [Fact]
    public void TheTitleAndJournalAreTranslatedLikeAnyOtherLine()
    {
        Compile(Delivery).Translatable
            .Select(t => t.Text)
            .Should().Contain(["Silk Spool", "Menissiah wants 2 Apple of Moradon."]);
    }

    [Fact]
    public void ATitleAndKillsLiveInTheSameBlock()
    {
        var program = Compile("""
            Bind Npc 16079

            Quest 61 "Silk Spool" needs any
                Journal "Kill something, anything."
                Kill 10 of 1671
                Kill 5 of 1672

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Program;

        program.Texts[0].Title.Should().Be("Silk Spool");
        program.Objectives[0].Rule.Should().Be(ObjectiveRule.Any);
        program.Objectives[0].Groups.Should().HaveCount(2);
    }

    [Fact]
    public void AQuestHasOneJournalEntry()
    {
        Compile("""
            Bind Npc 16079

            Quest 61 "Silk Spool"
                Journal "One."
                Journal "Two."

            On greeting
                Say "Hello."
                Topic "Nothing" goto close
            """).Diagnostics.Should().Contain(d => d.Id == DiagnosticId.DuplicateObjectives);
    }

    [Fact]
    public void EachNationReadsItsOwnTitle()
    {
        var program = Compile("""
            Bind Npc 24440 Zone 1 for karus
            Bind Npc 14440 Zone 2 for elmorad

            Quest 167
                Title for karus "Marauders of Darkland I"
                Title for elmorad "Marauders of Blue Feather Valley I"
                Journal "The hunt is repeatable."
                Kill 20 of 8002

            Requires player level >= 45

            Rewards
                Give 1000 experience
            """).Program;

        program.TextFor(167, 1)!.Title.Should().Be("Marauders of Darkland I");
        program.TextFor(167, 2)!.Title.Should().Be("Marauders of Blue Feather Valley I");
        program.TextFor(167, 2)!.Journal.Should().Be("The hunt is repeatable.");
        program.Texts.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(1, 24440, 1, "Marauders of Darkland I")]
    [InlineData(2, 14440, 2, "Marauders of Blue Feather Valley I")]
    public void TheNpcMenuListsTheQuestUnderThePlayersOwnTitle(int nation, int npc, int zone, string title)
    {
        var compilation = Compile("""
            Bind Npc 24440 Zone 1 for karus
            Bind Npc 14440 Zone 2 for elmorad

            Quest 167
                Title for karus "Marauders of Darkland I"
                Title for elmorad "Marauders of Blue Feather Valley I"
                Journal "The hunt is repeatable."
                Kill 20 of 8002

            Requires player level >= 45

            Rewards
                Give 1000 experience
            """);
        var program = QuestProgramComposer.Compose("board", npc, zone, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(zone);
        host.PlayerNation.Returns(nation);
        host.PlayerLevel.Returns(50);
        IReadOnlyList<DialogButton> shown = [];
        host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(),
                Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => shown = c.ArgAt<IReadOnlyList<DialogButton>>(3));

        program.TryGetEntry(QuestProgram.TopicsEvent, 0, out var topics).Should().BeTrue();
        new QuestInterpreter(program, host).Run(topics).Failure.Should().BeNull();

        shown.Select(b => b.Label.Text).Should().Equal(title);
    }

    [Theory]
    [InlineData("Quest 167 \"Marauders\"\n    Title for karus \"Marauders of Darkland I\"\n    Title for elmorad \"Marauders of Blue Feather Valley I\"")]
    [InlineData("Quest 167\n    Title for karus \"One\"\n    Title for karus \"Two\"\n    Title for elmorad \"Three\"")]
    [InlineData("Quest 167\n    Title for karus \"One\"\n    Journal for elmorad \"Two\"")]
    [InlineData("Quest 167\n    Title karus \"One\"")]
    public void PerNationTitlesAreWrittenOncePerNationAndForEveryNationWithAJournal(string block)
    {
        Compile("Bind Npc 24440 Zone 1 for karus\nBind Npc 14440 Zone 2 for elmorad\n\n" + block
            + "\n    Kill 20 of 8002\n\nRequires player level >= 45\n\nRewards\n    Give 1000 experience\n")
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public void TheFileBeatsTheCatalogWhenItNamesTheQuest()
    {
        var compilation = Compile(Delivery, new StaleTitles());
        var id = new ResolvedId(new TextSpan(0, 1), SlotKind.QuestId, 61);

        compilation.NameFor(id).Should().Be("Silk Spool");
    }

    [Fact]
    public void TheCatalogStillNamesAQuestTheFileDoesNot()
    {
        var compilation = Compile(Delivery, new StaleTitles());
        var id = new ResolvedId(new TextSpan(0, 1), SlotKind.QuestId, 62);

        compilation.NameFor(id).Should().Be("what the table used to say");
    }
}
