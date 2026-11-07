using System.Runtime.CompilerServices;
using FluentAssertions;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class AbyssGateTests
{
    private const int Delos = 30;
    private const int DesperationAbyss = 32;
    private const int HellAbyss = 33;
    private const int DungeonKey = 379080000;
    private const int KeyOfHell = 379111000;
    private const int TenthFloorKey = 379081000;
    private const string Down = "Go downstairs";
    private const string Up = "Go upstairs";
    private const string Leave = "Leave the abyss";
    private const string UseKey = "Use the teleport key";

    private static readonly Lazy<QuestProgram> Gate = new(() =>
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("7001.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        return compilation.Program;
    });

    private static string BakedQuestPath(string name, [CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private static IQuestHost AtGate(int trigger, params int[] items)
    {
        var host = Substitute.For<IQuestHost>();
        host.NpcTrigger.Returns(trigger);
        host.ItemCount(Arg.Any<int>()).Returns(call => items.Contains(call.Arg<int>()) ? 1 : 0);
        return host;
    }

    private static IReadOnlyList<DialogButton> Shown(IQuestHost host) =>
        (IReadOnlyList<DialogButton>)host.ReceivedCalls().Last(c => c.GetMethodInfo().Name == "ShowDialog").GetArguments()[3]!;

    private static void Run(IQuestHost host, int eventId) =>
        new QuestInterpreter(Gate.Value, host).Run(eventId).Failure.Should().BeNull();

    private static void Talk(IQuestHost host)
    {
        Gate.Value.TryGetEntry(QuestProgram.GreetingEvent, 0, out var greeting).Should().BeTrue();
        Run(host, greeting);
    }

    private static void Pick(IQuestHost host, string label) =>
        Run(host, Shown(host).Single(button => button.Label.Text == label).TargetEvent);

    [Theory]
    [InlineData(40001, DesperationAbyss, 50, 197)]
    [InlineData(40005, DesperationAbyss, 150, 237)]
    [InlineData(40019, DesperationAbyss, 450, 433)]
    [InlineData(42001, HellAbyss, 62, 226)]
    [InlineData(42009, HellAbyss, 315, 225)]
    public void GoingDownstairsLeadsToTheNextFloor(int trigger, int zone, int x, int z)
    {
        var host = AtGate(trigger);
        Talk(host);
        Pick(host, Down);
        host.Received(1).TeleportToZone(zone, x, z);
    }

    [Fact]
    public void GoingUpstairsLeadsToThePreviousFloor()
    {
        var host = AtGate(40005);
        Talk(host);
        Pick(host, Up);
        Pick(host, Up);
        host.Received(1).TeleportToZone(DesperationAbyss, 50, 437);
    }

    [Fact]
    public void TheFirstFloorLeadsOutToDelos()
    {
        var host = AtGate(40001);
        Talk(host);
        Pick(host, Leave);
        host.Received(1).TeleportToZone(Delos, 500, 250);
    }

    [Fact]
    public void AKeyFloorTakesTheDungeonKeyAndMovesTheParty()
    {
        var host = AtGate(40012, DungeonKey);
        Talk(host);
        Pick(host, Down);
        host.Received(1).TakeItem(DungeonKey, 1);
        host.Received(1).TeleportPartyToZone(DesperationAbyss, 350, 117);
    }

    [Fact]
    public void AKeyFloorWithoutTheKeyMovesNobody()
    {
        var host = AtGate(40012);
        Talk(host);
        Pick(host, Down);
        host.DidNotReceive().TeleportPartyToZone(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
        host.DidNotReceive().TeleportToZone(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public void TheTenthFloorKeySkipsToTheTenthFloor()
    {
        var host = AtGate(40003, TenthFloorKey);
        Talk(host);
        Pick(host, UseKey);
        Pick(host, "Transport to 10th floor of the Abyss of Despair");
        host.Received(1).TakeItem(TenthFloorKey, 1);
        host.Received(1).TeleportPartyToZone(DesperationAbyss, 250, 192);
    }

    [Fact]
    public void TheKeyOfHellOpensTheAbyssOfHell()
    {
        var host = AtGate(40007, KeyOfHell);
        Talk(host);
        Pick(host, "Use the Abyss of Hell");
        host.Received(1).TakeItem(KeyOfHell, 1);
        host.Received(1).TeleportPartyToZone(HellAbyss, 62, 91);
    }
}
