using FluentAssertions;
using NSubstitute;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;

namespace LibreKO.Game.Tests;

public class QuestTimedRewardTests
{
    private static (QuestProgram Program, IQuestHost Host, int Entry) Ready(string body)
    {
        var result = QuestCompilation.Create($"""
            Bind Npc 100
            Quest 61 "Timed"

            On fulfil
            {body}
            """, "timed.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var entry).Should().BeTrue();
        return (result.Program, host, entry);
    }

    [Fact]
    public void ALooseTimedGiveReachesTheHostInHours()
    {
        var (program, host, entry) = Ready("    Give 1 of 508473456 for 30 days");
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received().GiveItem(508473456, 1, 30 * QuestVocabulary.HoursPerDay);
    }

    [Fact]
    public void AGiveMeasuredInHoursReachesTheHostUnscaled()
    {
        var (program, host, entry) = Ready("    Give 1 of 508473456 for 6 hours");
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received().GiveItem(508473456, 1, 6);
    }

    [Fact]
    public void AnUntimedGiveAsksForNoExpiry()
    {
        var (program, host, entry) = Ready("    Give 1 of 508473456");
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        host.Received().GiveItem(508473456, 1, 0);
    }

    [Fact]
    public void ATransactionCarriesTheExpiryOfEachGive()
    {
        var (program, host, entry) = Ready("""
                Transaction
                    Take 1 of 379156000
                    Give 1 of 508473456 for 30 days
            """);
        new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
        var call = host.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IQuestHost.ApplyReward));
        var applied = (IReadOnlyList<BoundStatement.Action>)call.GetArguments()[0]!;
        var give = applied.Single(a => a.Kind == QuestActionKind.GiveItem);
        give.Arguments.GetInt("item").Should().Be(508473456);
        give.Arguments.GetInt("days").Should().Be(30);
    }
}
