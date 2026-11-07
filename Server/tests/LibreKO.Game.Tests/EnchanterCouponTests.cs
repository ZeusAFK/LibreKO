using System.Runtime.CompilerServices;
using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class EnchanterCouponTests
{
    private const int BuffCoupon = 900667000;
    private const int Moradon = 21;
    private const int StrongLegs = 490223;
    private const int ShoutForCharge = 302344;
    private const string Mount = "[Spirit Alseids] Of mounting";
    private const string Settlement = "Settlement support free of charge buff";

    private static QuestProgram Compile(string name, [CallerFilePath] string source = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));
        var compilation = QuestCompilation.CreateFromFile(path);
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return compilation.Program;
    }

    private static IQuestHost Player(int level, bool hasCoupon)
    {
        var host = Substitute.For<IQuestHost>();
        host.PlayerLevel.Returns(level);
        host.PlayerZone.Returns(Moradon);
        host.ItemCount(BuffCoupon).Returns(hasCoupon ? 1 : 0);
        return host;
    }

    private static IReadOnlyList<DialogButton> Shown(IQuestHost host) =>
        (IReadOnlyList<DialogButton>)host.ReceivedCalls().Last(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[3]!;

    private static void Pick(QuestProgram program, IQuestHost host, string label)
    {
        program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        new QuestInterpreter(program, host).Run(greeting).Failure.Should().BeNull();
        var target = Shown(host).Single(button => button.Label.Text == label).TargetEvent;
        new QuestInterpreter(program, host).Run(target).Failure.Should().BeNull();
    }

    [Theory]
    [InlineData("31505.quest", 20, 302331, 302328)]
    [InlineData("31507.quest", 50, 302332, 302329)]
    [InlineData("31508.quest", 75, 302333, 302330)]
    public void TheSettlementCouponBuysTheFourBuffsOfThePlayersLevel(string script, int level, int defence, int strength)
    {
        var host = Player(level, hasCoupon: true);

        Pick(Compile(script), host, Settlement);

        host.Received(1).TakeItem(BuffCoupon, 1);
        host.Received(1).CastSkill(ShoutForCharge);
        host.Received(1).CastSkill(defence);
        host.Received(1).CastSkill(strength);
        host.Received(1).CastSkill(StrongLegs);
    }

    [Theory]
    [InlineData("31505.quest")]
    [InlineData("31508.quest")]
    public void TheMountCouponBuysStrongLegs(string script)
    {
        var host = Player(40, hasCoupon: true);

        Pick(Compile(script), host, Mount);

        host.Received(1).TakeItem(BuffCoupon, 1);
        host.Received(1).CastSkill(StrongLegs);
    }

    [Fact]
    public void WithoutTheCouponNothingIsTakenOrCast()
    {
        var host = Player(40, hasCoupon: false);

        Pick(Compile("31505.quest"), host, Settlement);

        host.DidNotReceive().TakeItem(Arg.Any<int>(), Arg.Any<int>());
        host.DidNotReceive().CastSkill(Arg.Any<int>());
    }
}
