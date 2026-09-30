using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestRebirthChainTests
{
    private const int Mekin = 19005;
    private const int Moradon = 21;
    private const int ProofOfStrength = 1119;
    private const int ProofOfWealth = 1120;
    private const int ProofOfHonor = 1121;
    private const int Rebirth = 1122;
    private const int QualificationOfRebirth = 900579000;

    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static QuestProgram Compose(params string[] files) => Compose(Mekin, Moradon, files);

    private static QuestProgram Compose(int npc, int zone, params string[] files)
    {
        var programs = files.Select(file =>
        {
            var compilation = QuestCompilation.CreateFromFile(BakedQuestPath(file));
            compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
            return compilation.Program;
        }).ToList();
        return QuestProgramComposer.Compose("mekin", npc, zone, programs);
    }

    private static IQuestHost Level83(int rebirthLevel = 0, bool fullBar = true)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(83);
        host.PlayerZone.Returns(Moradon);
        host.PlayerNation.Returns(1);
        host.PlayerRebirthLevel.Returns(rebirthLevel);
        host.ReachedLevel(83, 100).Returns(fullBar);
        return host;
    }

    [Theory]
    [InlineData(0, true, QuestViewState.Available)]
    [InlineData(0, false, QuestViewState.Locked)]
    [InlineData(15, true, QuestViewState.Locked)]
    [InlineData(14, true, QuestViewState.Available)]
    public void TheFirstTestIsOfferedAtLevel83WithAFullBarBelowTheCap(int rebirthLevel, bool fullBar, QuestViewState expected)
    {
        var program = Compose("19005_21_1119.quest");
        var host = Level83(rebirthLevel, fullBar);
        new QuestInterpreter(program, host).BuildView(ProofOfStrength).State.Should().Be(expected);
    }

    [Fact]
    public void TheWealthTestCollectsTheGoldBarAndTheHonorTestTakesThePointsForTheQualification()
    {
        var wealth = Compose("19005_21_1120.quest").RewardsFor(ProofOfWealth, 0, 1);
        wealth.Should().NotBeNull();
        wealth!.Transfers.Should().ContainSingle(t => t.Kind == QuestActionKind.TakeGold && t.Arguments.GetInt("amount") == 100_000_000);

        var honor = Compose("19005_21_1121.quest").RewardsFor(ProofOfHonor, 0, 1);
        honor.Should().NotBeNull();
        honor!.Transfers.Should().ContainSingle(t => t.Kind == QuestActionKind.TakeNationalPoints && t.Arguments.GetInt("amount") == 10_000);
        honor.Transfers.Should().ContainSingle(t => t.Kind == QuestActionKind.GiveItem && t.Arguments.GetInt("item") == QualificationOfRebirth);
    }

    [Theory]
    [InlineData(9_999, QuestViewState.InProgress)]
    [InlineData(10_000, QuestViewState.Claimable)]
    public void TheHonorTestWaitsForTenThousandPoints(int loyalty, QuestViewState expected)
    {
        var program = Compose("19005_21_1121.quest");
        var host = Level83();
        host.QuestStatus(ProofOfWealth).Returns(2);
        host.QuestStatus(ProofOfHonor).Returns(1);
        host.PlayerLoyalty.Returns(loyalty);
        new QuestInterpreter(program, host).BuildView(ProofOfHonor).State.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void TheRebirthWindowOpensOnlyWhileTheQualificationIsHeld(int held, bool opens)
    {
        var program = Compose("19005_21_1122.quest");
        var host = Level83();
        host.QuestStatus(ProofOfHonor).Returns(2);
        host.QuestStatus(Rebirth).Returns(1);
        host.ItemCount(QualificationOfRebirth).Returns(held);
        var said = new List<string>();
        host.When(h => h.Say(Arg.Any<IReadOnlyList<DialogLine>>())).Do(c => said.AddRange(c.Arg<IReadOnlyList<DialogLine>>().Select(l => l.Text ?? "")));
        host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => said.Add(c.ArgAt<DialogLine>(2).Text ?? ""));
        host.When(h => h.ShowQuestView(Arg.Any<QuestView>())).Do(c => said.Add(c.Arg<QuestView>().Dialogue.Text ?? ""));

        var interpreter = new QuestInterpreter(program, host);
        var view = interpreter.BuildView(Rebirth);
        view.State.Should().Be(QuestViewState.InProgress, "Fulfil elsewhere keeps the rebirth quest open until the rebirth removes it");
        var reply = program.Events.Values.Single(e => e.Name is null).Id;
        new QuestInterpreter(program, host).Run(reply).Failure.Should().BeNull();
        host.Received(opens ? 1 : 0).OpenRebirthPanel();
        if (!opens)
            string.Join(" ", said).Should().Contain("do not qualify");
    }

    [Theory]
    [InlineData(9, QuestViewState.Available, QuestViewState.Locked)]
    [InlineData(10, QuestViewState.Locked, QuestViewState.Available)]
    public void FromTheTenthRebirthTheHonorTestBecomesTheFiveHundredMillionOne(int rebirthLevel, QuestViewState ordinary, QuestViewState advanced)
    {
        var host = Level83(rebirthLevel);
        host.QuestStatus(ProofOfWealth).Returns(2);
        new QuestInterpreter(Compose("19005_21_1121.quest"), host).BuildView(ProofOfHonor).State.Should().Be(ordinary);
        new QuestInterpreter(Compose("19005_21_1720.quest"), host).BuildView(1720).State.Should().Be(advanced);
    }

    [Theory]
    [InlineData(2, 1722, QuestViewState.Available)]
    [InlineData(1, 1722, QuestViewState.Locked)]
    [InlineData(1, 1723, QuestViewState.Available)]
    [InlineData(2, 1723, QuestViewState.Locked)]
    public void TheKillWayIsOfferedToItsOwnNationFromTheTenthRebirth(int nation, int quest, QuestViewState expected)
    {
        var host = Level83(10);
        host.PlayerNation.Returns(nation);
        host.QuestStatus(ProofOfWealth).Returns(2);
        new QuestInterpreter(Compose($"19005_21_{quest}.quest"), host).BuildView(quest).State.Should().Be(expected);
    }

    [Theory]
    [InlineData(1121, QuestViewState.Available)]
    [InlineData(1720, QuestViewState.Available)]
    [InlineData(1723, QuestViewState.Available)]
    [InlineData(0, QuestViewState.Locked)]
    public void TheRebirthQuestFollowsWhicheverLastTestWasPassed(int passed, QuestViewState expected)
    {
        var host = Level83();
        if (passed > 0) host.QuestStatus(passed).Returns(2);
        new QuestInterpreter(Compose("19005_21_1122.quest"), host).BuildView(Rebirth).State.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, QuestViewState.Locked)]
    [InlineData(1, QuestViewState.Available)]
    public void DasonsDailyWaitsForTheFirstRebirth(int rebirthLevel, QuestViewState expected)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(83);
        host.PlayerZone.Returns(71);
        host.PlayerNation.Returns(1);
        host.PlayerRebirthLevel.Returns(rebirthLevel);
        new QuestInterpreter(Compose(31743, 71, "31743_71_1631.quest"), host).BuildView(1631).State.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 931685000)]
    [InlineData(6, 931684000)]
    [InlineData(10, 931683000)]
    [InlineData(15, 931683000)]
    public void DasonsDailyPaysTheBoxOfTheRebirthTier(int rebirthLevel, int box)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(83);
        host.PlayerZone.Returns(71);
        host.PlayerNation.Returns(1);
        host.PlayerRebirthLevel.Returns(rebirthLevel);
        var view = new QuestInterpreter(Compose(31743, 71, "31743_71_1631.quest"), host).BuildView(1631);
        view.Rewards.Transfers.Where(a => a.Kind == QuestActionKind.GiveItem).Select(a => a.Arguments.GetInt("item")).Should().Equal(box);
    }

    [Fact]
    public void MekinGreetsWithTheFallingStarsAndAnswersAnEmptyMenuWithTheBarRule()
    {
        var program = Compose("19005_21.quest", "19005_21_1119.quest");
        var host = Level83(fullBar: false);
        IReadOnlyList<DialogLine>? said = null;
        DialogLine? header = null;
        host.When(h => h.Say(Arg.Any<IReadOnlyList<DialogLine>>())).Do(c => said = c.Arg<IReadOnlyList<DialogLine>>());
        host.When(h => h.ShowDialog(Arg.Any<DialogStyle>(), Arg.Any<int>(), Arg.Any<DialogLine>(), Arg.Any<IReadOnlyList<DialogButton>>()))
            .Do(c => header = c.ArgAt<DialogLine>(2));
        program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        var text = string.Join(" ", (said ?? (header is null ? [] : [header])).Select(l => l.Text));
        text.Should().Contain("level 83 with 100 % EXP");
    }
}
