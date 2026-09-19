using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests.Localization;
using Microsoft.Extensions.Logging;
using LibreKO.Quests;
using LibreKO.Quests.Binding;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestClanRankTests
{
    private static bool Runs(string condition, ClanType rank)
    {
        var compilation = QuestCompilation.Create(
            $"Bind Npc 11610\nOn greeting\n    If {condition}\n        Give 1 coins\n", "clan.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());

        var host = Substitute.For<IQuestHost>();
        host.ClanRank.Returns((int)rank);
        compilation.Program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(compilation.Program, host).Run(entry).Failure.Should().BeNull();
        return host.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IQuestHost.GiveGold));
    }

    [Fact]
    public void TheLanguagesRanksAreTheServersClanTypeExactly()
    {
        QuestVocabulary.ClanRanks
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Should().BeEquivalentTo(
                Enum.GetValues<ClanType>().ToDictionary(value => value.ToString(), value => (int)value),
                "the script's names are the clan flag CheckKnight returns");

        QuestVocabulary.ClanRankNames.Should().Equal(Enum.GetNames<ClanType>());
    }

    [Theory]
    [InlineData(ClanType.Royal1, true)]
    [InlineData(ClanType.Royal2, false)]
    [InlineData(ClanType.None, false)]
    public void ANamedRankMatchesOnlyItself(ClanType rank, bool expected) =>
        Runs("player clan is Royal1", rank).Should().Be(expected);

    [Theory]
    [InlineData(ClanType.Royal1, false)]
    [InlineData(ClanType.Promoted, true)]
    public void ARankCanBeExcluded(ClanType rank, bool expected) =>
        Runs("player clan is not Royal1", rank).Should().Be(expected);

    [Theory]
    [InlineData(ClanType.None, true)]
    [InlineData(ClanType.Training, true)]
    [InlineData(ClanType.Promoted, true)]
    [InlineData(ClanType.Accredited5, false)]
    [InlineData(ClanType.Royal1, false)]
    public void AnOrderedCompareCoversTheRanksBelowIt(ClanType rank, bool expected) =>
        Runs("player clan rank < Accredited5", rank).Should().Be(expected);

    [Fact]
    public void AccreditedOneOutranksAccreditedFive() =>
        Runs("player clan rank > Accredited5", ClanType.Accredited1).Should().BeTrue();

    [Fact]
    public void AnUnknownRankNamesTheOnesThatExist()
    {
        var compilation = QuestCompilation.Create(
            "Bind Npc 11610\nOn greeting\n    If player clan is Emperor\n        Give 1 coins\n", "clan.quest");

        compilation.Diagnostics.Should().Contain(d =>
            d.Id == DiagnosticId.UnknownEnumMember && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ThePromotionFlowChargesTheClanAndRaisesItsFlag()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 11610

            On greeting
                If player clan grade < 4 and player clan points >= 252000
                    Take 252000 clan points
                    Promote clan to Accredited4
            """, "promote.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());

        var host = Substitute.For<IQuestHost>();
        host.ClanGrade.Returns(1);
        host.ClanPoints.Returns(252_000);
        compilation.Program.TryGetGreeting(out var entry).Should().BeTrue();
        new QuestInterpreter(compilation.Program, host).Run(entry).Failure.Should().BeNull();

        Received.InOrder(() =>
        {
            host.TakeClanPoints(252_000);
            host.PromoteClan((int)ClanType.Accredited4);
        });
    }

    [Fact]
    public void ATooPoorClanIsNotChargedAndNotPromoted()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 11610

            On greeting
                If player clan points >= 252000
                    Take 252000 clan points
                    Promote clan to Accredited4
            """, "promote.quest");

        var host = Substitute.For<IQuestHost>();
        host.ClanPoints.Returns(251_999);
        compilation.Program.TryGetGreeting(out var entry);
        new QuestInterpreter(compilation.Program, host).Run(entry).Failure.Should().BeNull();

        host.DidNotReceiveWithAnyArgs().TakeClanPoints(default);
        host.DidNotReceiveWithAnyArgs().PromoteClan(default);
    }

    [Fact]
    public void PromotingAClanIsNotAPlayerJobChange()
    {
        var compilation = QuestCompilation.Create(
            "Bind Npc 11610\nOn greeting\n    Promote clan to Royal1\n", "promote.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());

        var host = Substitute.For<IQuestHost>();
        compilation.Program.TryGetGreeting(out var entry);
        new QuestInterpreter(compilation.Program, host).Run(entry);

        host.Received(1).PromoteClan((int)ClanType.Royal1);
        host.DidNotReceive().PromotePlayer();
    }

    [Fact]
    public void TheRealHostReadsTheClansOwnFlag()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        var context = new QuestScriptContext(
            session, null, Substitute.For<IGameDataService>(), sessions,
            Substitute.For<ILogger>(), 1);
        var host = new QuestScriptHost(
            session, context, Substitute.For<IQuestTranslations>(),
            Substitute.For<ILogger>(), "clan.quest");

        host.ClanRank.Should().Be(context.ClanParty.CheckKnight(0));
        host.ClanRank.Should().Be((int)ClanType.None, "a clanless player has no flag");
    }
}
