using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestClanPremiumTests
{
    private const short ClanId = 42;

    [Fact]
    public void AClanPremiumGrantReachesTheHostWithTheDaysAlone()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100

            On greeting
                Give clan premium for 30 days
            """, "clanpremium.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetGreeting(out var entry).Should().BeTrue();

        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();

        host.Received().GiveClanPremium(30);
    }

    [Fact]
    public void OnlyTheLeaderCanBuyIt()
    {
        var (context, session, _) = CreateClan(leader: false);

        context.ClanParty.GiveClanPremium(0, 30).Should().BeFalse();

        context.PremiumClanId.Should().Be(0);
        session.KnightsId.Should().Be(ClanId);
    }

    [Fact]
    public void TheLeaderExtendsWhatTheClanHasLeft()
    {
        var (context, _, clan) = CreateClan(leader: true);
        clan.PremiumExpiry = DateTime.UtcNow.AddDays(5);

        context.ClanParty.GiveClanPremium(0, 30).Should().BeTrue();

        clan.PremiumExpiry.Should().BeCloseTo(DateTime.UtcNow.AddDays(35), TimeSpan.FromMinutes(1));
        clan.HasPremium.Should().BeTrue();
        context.PremiumClanId.Should().Be(ClanId);
    }

    [Fact]
    public void AnExpiredClanPremiumStartsAgainFromNow()
    {
        var (context, _, clan) = CreateClan(leader: true);
        clan.PremiumExpiry = DateTime.UtcNow.AddDays(-5);

        context.ClanParty.GiveClanPremium(0, 7).Should().BeTrue();

        clan.PremiumExpiry.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
    }

    private static (QuestScriptContext Context, UserSession Session, KnightsEntity Clan) CreateClan(bool leader)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);
        session.KnightsId = ClanId;
        session.KnightsFame = leader ? (byte)1 : (byte)5;

        var clan = new KnightsEntity { Id = ClanId, Name = "Aurelians", Chief = "Leader" };
        sessionManager.Knights.AddClan(ClanId, clan);

        var context = new QuestScriptContext(session, npc: null, Substitute.For<IGameDataService>(),
            sessionManager, Substitute.For<ILogger>(), expMultiplier: 1);
        return (context, session, clan);
    }
}
