using FluentAssertions;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using NSubstitute;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace LibreKO.Game.Tests;

public class QuestAutomaticFlowTests
{
    private static string BakedQuestPath(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    [Fact]
    public void BakedBandicootRetainsConversationPagesDirectionsAndOneSharedClaim()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21_62.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("patrick", 13013, 21, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(3);
        host.When(h => h.SetQuestState(62, Arg.Any<int>())).Do(c => host.QuestStatus(62).Returns(c.ArgAt<int>(1)));
        QuestView? current = null;
        host.When(h => h.ShowQuestView(Arg.Any<QuestView>())).Do(c => current = c.Arg<QuestView>());
        host.When(h => h.UpdateQuestView(Arg.Any<QuestView>())).Do(c => current = c.Arg<QuestView>());

        void Enter(string role)
        {
            program.TryGetEntry(role, 62, out var entry).Should().BeTrue();
            new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        }

        Enter(QuestProgram.ViewEvent);
        current!.Dialogue.Text.Should().Contain("look like rats").And.NotContain("Patrick's hiring more guards.");
        current.Page.Should().Be(QuestPageKind.Conversation);
        var explanation = current.Topics.Single(t => t.Label.Text == "Yes, I just saw him");
        new QuestInterpreter(program, host).Run(explanation.TargetEvent).Failure.Should().BeNull();
        current!.State.Should().Be(QuestViewState.Available);
        current.Page.Should().Be(QuestPageKind.Quest);
        current.Dialogue.Text.Should().Contain("They're called Bandicoots.");
        host.DidNotReceiveWithAnyArgs().SetQuestState(default, default);
        host.DidNotReceiveWithAnyArgs().ApplyReward(default!);

        Enter(QuestProgram.AcceptEvent);
        host.Received().UpdateQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.InProgress));
        host.DidNotReceive().ShowQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.InProgress));

        Enter(QuestProgram.ViewEvent);
        current!.State.Should().Be(QuestViewState.InProgress);
        current.Page.Should().Be(QuestPageKind.Conversation);
        current.Dialogue.Text.Should().Contain("still haven't caught all 5");
        var directions = current.Topics.Single(t => t.Label.Text == "Where can I find it?");
        new QuestInterpreter(program, host).Run(directions.TargetEvent).Failure.Should().BeNull();
        host.Received(1).ShowLocation(Arg.Any<QuestLocation>(), 62);

        host.KillCount(62, 1).Returns(5);
        Enter(QuestProgram.ViewEvent);
        current!.State.Should().Be(QuestViewState.Claimable);
        current.Page.Should().Be(QuestPageKind.Quest);
        current.Dialogue.Text.Should().Contain("Wow, I underestimated you.").And.NotContain("Whoa, Master");

        program.TryGetEntry(QuestProgram.ReadyEvent, 62, out var ready).Should().BeTrue();
        new QuestInterpreter(program, host).Run(ready).Failure.Should().BeNull();
        current!.Page.Should().Be(QuestPageKind.Conversation);
        current.Dialogue.Text.Should().Contain("Whoa, Master").And.NotContain("Wow, I underestimated you.");
        current.Topics.Single().Label.Text.Should().Be("All right, I'll swing by");

        Enter(QuestProgram.FulfilEvent);
        Enter(QuestProgram.FulfilEvent);
        host.Received(1).ApplyReward(program.QuestRewards.Single().Transfers);
        host.Received(1).ShowReceipt(62);
        host.Received().UpdateQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.Completed));
        host.DidNotReceive().ShowQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.Completed));

        Enter(QuestProgram.ViewEvent);
        current!.State.Should().Be(QuestViewState.Completed);
        current.Dialogue.Text.Should().Contain("You are better than you look.");
    }

    [Fact]
    public void TheFirstJobChangePaysTheFeeAndPromotesWithoutHandingOverAToken()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("18004_21_71.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("kaishan", 18004, 21, [compilation.Program]);

        var transfers = program.QuestRewards.Single().Transfers;
        transfers.Should().Contain(a => a.Kind == LibreKO.Quests.Binding.QuestActionKind.PromoteNovice);
        transfers.Should().NotContain(a =>
            a.Kind == LibreKO.Quests.Binding.QuestActionKind.GiveItem
            && a.Arguments.GetInt("item") == 900006000);

        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(10);
        host.When(h => h.SetQuestState(71, Arg.Any<int>())).Do(c => host.QuestStatus(71).Returns(c.ArgAt<int>(1)));

        program.TryGetEntry(QuestProgram.AcceptEvent, 71, out var accept).Should().BeTrue();
        new QuestInterpreter(program, host).Run(accept).Failure.Should().BeNull();
        program.TryGetEntry(QuestProgram.FulfilEvent, 71, out var fulfil).Should().BeTrue();
        new QuestInterpreter(program, host).Run(fulfil).Failure.Should().BeNull();

        host.Received(1).ApplyReward(Arg.Is<IReadOnlyList<LibreKO.Quests.Runtime.BoundStatement.Action>>(
            a => a.Any(x => x.Kind == LibreKO.Quests.Binding.QuestActionKind.PromoteNovice)
                 && a.Any(x => x.Kind == LibreKO.Quests.Binding.QuestActionKind.TakeGold)));
        host.Received(1).SetQuestState(71, 2);
    }

    [Theory]
    [InlineData(1, "Skaki", 810090000, 810094000)]
    [InlineData(2, "Clarence", 810092000, 810093000)]
    [InlineData(3, "Drake", 810091000, 810092000)]
    [InlineData(4, "Minerva", 810091000, 810093000)]
    public void TheSecondJobChangeGivesEachClassItsOwnMasterJournalAndOfferings(
        int classGroup, string master, int essence, int second)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("0_0_273.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("masters", 14201, 2, [compilation.Program]);

        program.TextFor(273, 2, classGroup)!.Journal.Should().Contain(master);
        var rewards = program.RewardsFor(273, classGroup)!;
        rewards.Transfers.Should().Contain(a =>
            a.Kind == LibreKO.Quests.Binding.QuestActionKind.TakeItem && a.Arguments.GetInt("item") == essence);
        rewards.Transfers.Should().Contain(a =>
            a.Kind == LibreKO.Quests.Binding.QuestActionKind.TakeItem && a.Arguments.GetInt("item") == 810095000);
        rewards.Transfers.Should().Contain(a => a.Kind == LibreKO.Quests.Binding.QuestActionKind.Promote);

        rewards.Transfers.Where(a => a.Kind == LibreKO.Quests.Binding.QuestActionKind.TakeItem)
            .Select(a => a.Arguments.GetInt("item")).Should()
            .BeEquivalentTo(new[] { 810095000, essence, second });

        foreach (var other in new[] { 1, 2, 3, 4 }.Where(g => g != classGroup))
            program.RewardsFor(273, other)!.Transfers
                .Where(a => a.Kind == LibreKO.Quests.Binding.QuestActionKind.TakeItem)
                .Select(a => a.Arguments.GetInt("item")).Should()
                .NotBeEquivalentTo(new[] { 810095000, essence, second });

        var flow = program.Flows.Single(f => f.QuestId == 273);
        flow.BindingFor(2, 2, classGroup)!.NpcId.Should().Be(14200 + classGroup);
        flow.BindingFor(1, 1, classGroup)!.NpcId.Should().Be(24200 + classGroup);
    }

    [Fact]
    public void EveryBakedRewardTransferSurvivesTheQuestViewWire()
    {
        var directory = Path.GetDirectoryName(BakedQuestPath("quest-manifest.json"))!;
        var manifest = System.Text.Json.JsonSerializer.Deserialize<string[]>(
            File.ReadAllText(Path.Combine(directory, "quest-manifest.json")))!;

        var offenders = new List<string>();
        foreach (var name in manifest.Where(n => n.EndsWith(".quest")))
        {
            var compilation = QuestCompilation.CreateFromFile(Path.Combine(directory, name));
            if (!compilation.Succeeded) continue;
            foreach (var rewards in compilation.Program.QuestRewards)
                foreach (var action in rewards.Transfers.Concat(rewards.Options))
                {
                    var kind = QuestPacketWriter.TransferKind(action.Kind);
                    var item = action.Arguments.GetInt("item");
                    // The client rejects a transfer whose kind is 0 unless it names a real item.
                    if (kind == 0 && item <= 0)
                        offenders.Add($"{name} quest {rewards.QuestId}: {action.Kind} has no wire kind");
                    if (kind != 0 && item != 0)
                        offenders.Add($"{name} quest {rewards.QuestId}: {action.Kind} sets both kind and item");
                }
        }

        offenders.Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(13, true)]
    public void ThePortuSecondJobIsOfferedOnlyToPortu(int classGroup, bool offered)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("0_0_1371.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("elders", 25003, 1, [compilation.Program]);

        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(1);
        host.PlayerNation.Returns(1);
        host.PlayerLevel.Returns(60);
        host.PlayerClassGroup.Returns(classGroup);
        IReadOnlyList<DialogButton> shown = [];
        host.When(h => h.ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), Arg.Any<int>(),
            Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => shown = c.ArgAt<IReadOnlyList<DialogButton>>(3));

        program.TryGetEntry(QuestProgram.TopicsEvent, 0, out var topics).Should().BeTrue();
        new QuestInterpreter(program, host).Run(topics).Failure.Should().BeNull();

        shown.Any(b => b.Label.Text == "2nd job").Should().Be(offered);
    }

    [Theory]
    [InlineData(3, "What mission are you going to undertake?", "Bandicoot hunt")]
    [InlineData(2, "There are too many sentinels around.. Come back some other time..", "Close")]
    public void TheNpcGreetingOpensTheQuestListAndStandsAloneWhenNothingQualifies(
        int level, string expected, string choice)
    {
        var greeting = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21.quest"));
        var hunt = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21_62.quest"));
        greeting.Succeeded.Should().BeTrue(greeting.RenderDiagnostics());
        hunt.Succeeded.Should().BeTrue(hunt.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("patrick", 13013, 21, [greeting.Program, hunt.Program]);

        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(level);
        DialogLine? header = null;
        IReadOnlyList<DialogButton> offered = [];
        host.When(h => h.ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), Arg.Any<int>(),
            Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>())).Do(c =>
        {
            header = c.ArgAt<DialogLine>(2);
            offered = c.ArgAt<IReadOnlyList<DialogButton>>(3);
        });

        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        header!.Text.Should().Be(expected);
        offered.Should().ContainSingle(b => b.Label.Text == choice);
    }

    [Theory]
    [InlineData(0, 3, true)]
    [InlineData(4, 3, true)]
    [InlineData(1, 3, true)]
    [InlineData(3, 3, true)]
    [InlineData(2, 3, false)]
    [InlineData(0, 2, false)]
    [InlineData(1, 2, true)]
    public void TheNpcListsAQuestOnlyWhileItIsActiveOrStartable(int status, int level, bool listed)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21_62.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("patrick", 13013, 21, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(level);
        host.QuestStatus(62).Returns(status);
        IReadOnlyList<DialogButton> offered = [];
        host.When(h => h.ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), Arg.Any<int>(),
            Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => offered = c.ArgAt<IReadOnlyList<DialogButton>>(3));

        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        offered.Any(b => b.Label.Text!.EndsWith("Bandicoot hunt")).Should().Be(listed);
    }

    [Theory]
    [InlineData(0, 0, "Bandicoot hunt")]
    [InlineData(1, 0, "[In progress] Bandicoot hunt")]
    [InlineData(1, 5, "[Ready] Bandicoot hunt")]
    [InlineData(3, 5, "[Ready] Bandicoot hunt")]
    public void AnActiveQuestSaysSoInTheNpcMenu(int status, int kills, string label)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21_62.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("patrick", 13013, 21, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(3);
        host.QuestStatus(62).Returns(status);
        host.KillCount(62, Arg.Any<int>()).Returns(kills);
        IReadOnlyList<DialogButton> offered = [];
        host.When(h => h.ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), Arg.Any<int>(),
            Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => offered = c.ArgAt<IReadOnlyList<DialogButton>>(3));

        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();

        offered.Select(b => b.Label.Text).Should().Contain(label);
    }

    [Fact]
    public void NoQuestTitleCarriesRetailsStageSuffix()
    {
        var directory = Path.GetDirectoryName(BakedQuestPath("any.quest"))!;
        var suffix = new Regex(@"(Quest \d+|Topic) ""[^""]*\(\d+\)""");

        var offenders = Directory.EnumerateFiles(directory, "*.quest")
            .SelectMany(file => suffix.Matches(File.ReadAllText(file))
                .Select(match => $"{Path.GetFileName(file)}: {match.Value}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 0, "Unfinished Hunt II")]
    [InlineData(1, 0, "[In progress] Unfinished Hunt II")]
    [InlineData(1, 40, "[Ready] Unfinished Hunt II")]
    public void AConvertedHuntAlsoSaysWhereItStands(int status, int kills, string label)
    {
        var hunt = QuestCompilation.CreateFromFile(BakedQuestPath("14424_12_344.quest"));
        hunt.Succeeded.Should().BeTrue(hunt.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("tabeth", 14424, 12, [hunt.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(12);
        host.PlayerLevel.Returns(80);
        host.PlayerNation.Returns(2);
        host.QuestStatus(344).Returns(status);
        host.KillCount(344, Arg.Any<int>()).Returns(kills);
        IReadOnlyList<DialogButton> offered = [];
        host.When(h => h.ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), Arg.Any<int>(),
            Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => offered = c.ArgAt<IReadOnlyList<DialogButton>>(3));

        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();

        offered.Select(b => b.Label.Text).Should().Contain(label);
    }

    [Theory]
    [InlineData(19, false, false)]
    [InlineData(20, false, true)]
    [InlineData(20, true, false)]
    public void PatricksTwoHuntsShareOneGreetingAndListIndependently(int level, bool claimed, bool glyptodont)
    {
        var greeting = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21.quest"));
        var bandicoot = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21_62.quest"));
        var hunt = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21_98.quest"));
        foreach (var compilation in new[] { greeting, bandicoot, hunt })
            compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("patrick", 13013, 21,
            [greeting.Program, bandicoot.Program, hunt.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(level);
        host.QuestStatus(62).Returns(2);
        host.QuestStatus(98).Returns(claimed ? 2 : 0);
        IReadOnlyList<DialogButton> offered = [];
        host.When(h => h.ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), Arg.Any<int>(),
            Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => offered = c.ArgAt<IReadOnlyList<DialogButton>>(3));

        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();

        offered.Any(b => b.Label.Text == "Glyptodont hunt").Should().Be(glyptodont);
        offered.Any(b => b.Label.Text == "Bandicoot hunt").Should().BeFalse();
    }

    [Fact]
    public void TheGlyptodontHuntKeepsItsOwnTargetAndRetailText()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21_98.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var goals = compilation.Program.Objectives.Single(o => o.QuestId == 98);
        goals.Groups.Single().Monsters.Should().Equal(450);
        goals.Groups.Single().Count.Should().Be(1);
        compilation.Program.TryGetLocation(goals.Groups[0].Target, out var location).Should().BeTrue();
        location.Title.Should().Be("Glyptodon");
        (location.X, location.Y).Should().Be((586L, 573L));

        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(21);
        host.PlayerLevel.Returns(20);
        host.QuestStatus(98).Returns(1);
        host.KillCount(98, 1).Returns(1);
        var program = QuestProgramComposer.Compose("patrick", 13013, 21, [compilation.Program]);
        var view = new QuestInterpreter(program, host).BuildView(98);
        view.State.Should().Be(QuestViewState.Claimable);
        view.Text.Title.Should().Be("Glyptodont hunt");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(2, true)]
    public void AQuestGatedOnAnEarlierOneWaitsUntilThatOneIsClaimed(int earlier, bool offered)
    {
        var compilation = QuestCompilation.Create(PrerequisiteScript, "stage.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("stage", 31550, 1, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(1);
        host.PlayerLevel.Returns(20);
        host.QuestStatus(433).Returns(earlier);
        host.QuestStatus(434).Returns(0);

        new QuestInterpreter(program, host).BuildView(434).State.Should().Be(
            offered ? QuestViewState.Available : QuestViewState.Locked);
    }

    private const string PrerequisiteScript = """
        Bind Npc 31550 Zone 1
        Quest 434 "Buy Mattock"
            Journal "Buy a mattock now that the free one is gone."
            Kill 1 of 850

        Requires player level >= 20 and quest 433 is completed

        Rewards
            Give 100 experience
        """;

    [Theory]
    [InlineData(1, "[Imperial Guard] Bekar has commissioned you to hunt the Groom Hound.")]
    [InlineData(2, "[Imperial Guard] Telson has commissioned you to hunt the Groom Hound.")]
    public void OneQuestAtTwoNpcsGivesEachNationItsOwnJournal(int nation, string journal)
    {
        var compilation = QuestCompilation.Create(TwoNationScript, "272.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        compilation.Program.Bindings.Should().BeEquivalentTo(new[]
        {
            new QuestBinding(24406, 1, 1),
            new QuestBinding(14406, 2, 2),
        });

        var npc = nation == 1 ? 24406 : 14406;
        var zone = nation == 1 ? 1 : 2;
        var program = QuestProgramComposer.Compose("groom", npc, zone, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(zone);
        host.PlayerLevel.Returns(50);
        host.PlayerNation.Returns(nation);
        host.QuestStatus(272).Returns(1);

        var view = new QuestInterpreter(program, host).BuildView(272);
        view.Text.Journal.Should().Be(journal);
        view.Text.Title.Should().Be("Groom Hound Hunt");
    }

    [Theory]
    [InlineData(1, 31562, 1, "Zarg")]
    [InlineData(2, 31561, 2, "Halon")]
    public void BakedIntroductionCompletesOnceAndUnlocksPreparation(int nation, int npc, int zone, string name)
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("0_0_663.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("hunter", npc, zone, [compilation.Program]);
        var host = Substitute.For<IQuestHost>();
        host.PlayerNation.Returns(nation);
        host.PlayerZone.Returns(zone);
        host.PlayerLevel.Returns(61);
        host.When(h => h.SetQuestState(663, Arg.Any<int>()))
            .Do(c => host.QuestStatus(663).Returns(c.ArgAt<int>(1)));
        var interpreter = new QuestInterpreter(program, host);
        interpreter.BuildView(663).Text.Journal.Should().Contain(name);
        interpreter.BuildView(663).ZoneId.Should().Be(zone);
        var flow = program.Flows.Single();
        flow.BindingFor(nation, zone)!.NpcId.Should().Be(npc);
        flow.Matches(nation, zone).Should().BeTrue();
        flow.Matches(nation, zone == 1 ? 2 : 1).Should().BeFalse();

        void Enter(string role)
        {
            program.TryGetEntry(role, 663, out var entry).Should().BeTrue();
            new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        }

        Enter(QuestProgram.FulfilEvent);
        host.DidNotReceiveWithAnyArgs().SetQuestState(default, default);
        host.PlayerZone.Returns(zone == 1 ? 2 : 1);
        Enter(QuestProgram.AcceptEvent);
        host.DidNotReceiveWithAnyArgs().SetQuestState(default, default);
        host.PlayerZone.Returns(zone);
        Enter(QuestProgram.AcceptEvent);
        new QuestInterpreter(program, host).BuildView(663).State.Should().Be(QuestViewState.Claimable);
        Enter(QuestProgram.FulfilEvent);
        Enter(QuestProgram.FulfilEvent);
        host.Received(1).SetQuestState(663, 2);
        host.DidNotReceiveWithAnyArgs().ShowReceipt(default);
        compilation.Program.QuestRewards.Single().Transfers.Should().BeEmpty();

        var next = QuestCompilation.CreateFromFile(BakedQuestPath("0_0_664.quest"));
        next.Succeeded.Should().BeTrue(next.RenderDiagnostics());
        var preparation = QuestProgramComposer.Compose("hunter", npc, zone, [next.Program]);
        new QuestInterpreter(preparation, host).BuildView(664).State.Should().Be(QuestViewState.Available);
        host.QuestStatus(663).Returns(1);
        new QuestInterpreter(preparation, host).BuildView(664).State.Should().Be(QuestViewState.Locked);
    }

    [Fact]
    public void SameZoneNationBindingsCannotAcceptAtTheOtherNationsNpc()
    {
        var source = "Bind Npc 100 Zone 21 for karus\nBind Npc 200 Zone 21 for elmorad\nQuest 44 \"Meeting\"\nRewards none";
        var compiled = QuestCompilation.Create(source, "meeting.quest");
        compiled.Succeeded.Should().BeTrue(compiled.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        host.PlayerNation.Returns(1);
        host.PlayerZone.Returns(21);
        var wrong = QuestProgramComposer.Compose("other", 200, 21, [compiled.Program]);
        wrong.TryGetEntry(QuestProgram.AcceptEvent, 44, out var entry).Should().BeTrue();
        new QuestInterpreter(wrong, host).Run(entry);
        host.DidNotReceiveWithAnyArgs().SetQuestState(default, default);
        var right = QuestProgramComposer.Compose("own", 100, 21, [compiled.Program]);
        right.TryGetEntry(QuestProgram.AcceptEvent, 44, out entry).Should().BeTrue();
        new QuestInterpreter(right, host).Run(entry);
        host.Received(1).SetQuestState(44, 1);
    }

    [Theory]
    [InlineData("Rewards none extra")]
    [InlineData("Rewards none\n    Give 10 coins")]
    [InlineData("Rewards none\nRewards none")]
    [InlineData("Rewards")]
    public void RewardlessDeclarationCannotHideMalformedRewardBlocks(string declaration)
    {
        var source = "Bind Npc 100 Zone 21\nQuest 55 \"Introduction\"\n" + declaration;
        QuestCompilation.Create(source, "introduction.quest").Succeeded.Should().BeFalse();
    }

    private const string TwoNationScript = """
        Bind Npc 24406 Zone 1 for karus
        Bind Npc 14406 Zone 2 for elmorad

        Quest 272 "Groom Hound Hunt"
            Journal for karus "[Imperial Guard] Bekar has commissioned you to hunt the Groom Hound."
            Journal for elmorad "[Imperial Guard] Telson has commissioned you to hunt the Groom Hound."
            Kill 1 of 700

        Requires player level >= 50

        Rewards
            Give 1000 experience
        """;

    [Fact]
    public void TheHuntObjectiveKnowsWhereItsTargetLives()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("13013_21_62.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var goals = compilation.Program.Objectives.Single(o => o.QuestId == 62);
        compilation.Program.TryGetLocation(goals.Groups[0].Target, out var location).Should().BeTrue();
        location.Title.Should().Be("Bandicoot");
        location.About.Should().StartWith("Bandicoot\n").And.Contain("A rodent which grew abnormally");
        location.About.Should().NotContain("\\n");
        (location.X, location.Y).Should().Be((632L, 465L));
        (location.ElMoradX, location.ElMoradY).Should().Be((632L, 465L));
    }

    private const string Source = """
        Bind Npc 100 Zone 71
        Quest 1 "A hunt"
            Journal "Find the beast."
            Kill 1 of 10800
        Requires player level >= 61 and player has >= 1 of 123
        Rewards
            Give 100 coins
        On offer
            Say "Will you help?"
            Topic "Directions" do
                Show quest "North of town."
        On in_progress
            Say "Keep looking."
        On claimable
            Show quest "Well done."
        On completed
            Say "Thank you."
        """;

    private static QuestProgram Program()
    {
        var compilation = QuestCompilation.Create(Source, "hunt.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return QuestProgramComposer.Compose("npc", 100, 71, [compilation.Program]);
    }

    private static IQuestHost Host(int status = 0, int kills = 0, int unlock = 1)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerZone.Returns(71);
        host.PlayerLevel.Returns(61);
        host.QuestStatus(1).Returns(status);
        host.KillCount(1, 1).Returns(kills);
        host.ItemCount(123).Returns(unlock);
        host.When(h => h.SetQuestState(1, Arg.Any<int>())).Do(c => host.QuestStatus(1).Returns(c.ArgAt<int>(1)));
        return host;
    }

    private static void Run(QuestProgram program, IQuestHost host, string role)
    {
        program.TryGetEntry(role, 1, out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
    }

    [Fact]
    public void AStateThatAcceptsOrClaimsCannotBeAConversationDeadEnd()
    {
        var source = Source.Replace("Show quest \"Well done.\"", "Say \"Well done.\"");
        var compilation = QuestCompilation.Create(source, "hunt.quest");
        compilation.Succeeded.Should().BeFalse();
        compilation.RenderDiagnostics().Should().Contain("needs the quest page");
    }

    [Fact]
    public void NpcListsTitleAndSelectionOnlyOpensTheQuest()
    {
        var program = Program();
        var host = Host();
        Run(program, host, QuestProgram.GreetingEvent);
        host.Received().ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), -1, Arg.Any<DialogLine>(),
            Arg.Is<IReadOnlyList<DialogButton>>(b => b.Count == 1 && b[0].Label.Text == "A hunt"));
        Run(program, host, QuestProgram.ViewEvent);
        host.Received().ShowQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.Available
            && v.Dialogue.Text == "Will you help?" && v.Topics.Count == 1));
        host.DidNotReceiveWithAnyArgs().SetQuestState(default, default);
        host.DidNotReceiveWithAnyArgs().ApplyReward(default!);
    }

    [Fact]
    public void StateTopicRechecksTheStateWhenClicked()
    {
        var program = Program();
        var host = Host();
        Run(program, host, QuestProgram.ViewEvent);
        var view = (QuestView)host.ReceivedCalls().Single(c => c.GetMethodInfo().Name == "ShowQuestView").GetArguments()[0]!;
        host.ClearReceivedCalls();
        host.QuestStatus(1).Returns(2);
        new QuestInterpreter(program, host).Run(view.Topics.Single().TargetEvent).Failure.Should().BeNull();
        host.DidNotReceiveWithAnyArgs().ShowQuestView(default!);
    }

    [Fact]
    public void MetadataOnlyQuestHasADefaultViewAndAutomaticTopicsJoinServiceChoices()
    {
        var quest = QuestCompilation.Create("Bind Npc 100\nQuest 1\nRewards\n    Give 1 coins\n", "quest.quest");
        quest.Succeeded.Should().BeTrue(quest.RenderDiagnostics());
        var service = QuestCompilation.Create("Bind Npc 100\nOn greeting\n    Say \"Welcome\"\n    Topic \"Service\" goto close\n", "service.quest");
        service.Succeeded.Should().BeTrue(service.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("npc", 100, 71, [quest.Program, service.Program]);
        var host = Host();
        Run(program, host, QuestProgram.GreetingEvent);
        host.Received().ShowDialog(Arg.Any<LibreKO.Quests.Binding.DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(),
            Arg.Is<IReadOnlyList<DialogButton>>(b => b.Count == 2));
        Run(program, host, QuestProgram.ViewEvent);
        host.Received().ShowQuestView(Arg.Is<QuestView>(v => v.Text.Title == "Quest 1" && v.State == QuestViewState.Available));
    }

    [Fact]
    public void AcceptanceRechecksRequirementsAndOnlyStartsOneRun()
    {
        var program = Program();
        var host = Host(unlock: 0);
        Run(program, host, QuestProgram.AcceptEvent);
        host.DidNotReceiveWithAnyArgs().SetQuestState(default, default);
        host.ItemCount(123).Returns(1);
        Run(program, host, QuestProgram.AcceptEvent);
        Run(program, host, QuestProgram.AcceptEvent);
        host.Received(1).SetQuestState(1, 1);
        host.Received().UpdateQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.InProgress));
        host.DidNotReceive().ShowQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.InProgress));
    }

    [Theory]
    [InlineData(1, 0, QuestViewState.InProgress, "Keep looking.")]
    [InlineData(3, 0, QuestViewState.InProgress, "Keep looking.")]
    [InlineData(1, 1, QuestViewState.Claimable, "Well done.")]
    [InlineData(2, 1, QuestViewState.Completed, "Thank you.")]
    public void AcceptedQuestStaysAccessibleAfterUnlockIsConsumedAndReadinessUsesActualGoals(
        int status, int kills, QuestViewState state, string dialogue)
    {
        var program = Program();
        var host = Host(status, kills, 0);
        Run(program, host, QuestProgram.ViewEvent);
        host.Received().ShowQuestView(Arg.Is<QuestView>(v => v.State == state && v.Dialogue.Text == dialogue));
    }

    [Fact]
    public void ClaimUsesOnePlanAndDoesNotRecheckConsumedEntryRequirement()
    {
        var program = Program();
        var host = Host(1, 1, 0);
        Run(program, host, QuestProgram.FulfilEvent);
        Run(program, host, QuestProgram.FulfilEvent);
        host.Received(1).ApplyReward(program.QuestRewards.Single().Transfers);
        host.Received(1).SetQuestState(1, 2);
        host.Received().UpdateQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.Completed));
        host.DidNotReceive().ShowQuestView(Arg.Is<QuestView>(v => v.State == QuestViewState.Completed));
    }

    [Fact]
    public void OpeningAStatePageCannotExecuteAnUnconditionalPayout()
    {
        var compilation = QuestCompilation.Create(Source.Replace("Show quest \"Well done.\"", "Give 100 coins"), "bad.quest");
        compilation.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void StateHandlersAndCustomTopicsStayQuestScopedAfterComposition()
    {
        var first = QuestCompilation.Create(Source, "one.quest");
        var second = QuestCompilation.Create(Source.Replace("Quest 1 ", "Quest 2 ").Replace("Will you help?", "Second"), "two.quest");
        second.Succeeded.Should().BeTrue(second.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("npc", 100, 71, [first.Program, second.Program]);
        var host = Host();
        program.TryGetEntry(QuestProgram.ViewEvent, 2, out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received().ShowQuestView(Arg.Is<QuestView>(v => v.Text.QuestId == 2 && v.Dialogue.Text == "Second"));
    }
}
