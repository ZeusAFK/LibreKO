using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestPremiumTests
{
    private const int WarPremium = 12;

    [Fact]
    public void APremiumGrantReachesTheHost()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 100
            Quest 61 "Premium"

            On fulfil
                Give premium 12 for 30 days
            """, "premium.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetEntry(QuestProgram.FulfilEvent, 61, out var entry).Should().BeTrue();

        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();

        host.Received().GivePremium(WarPremium, 30);
    }

    [Fact]
    public void PremiumOfTheSameTypeExtendsWhatIsLeftRatherThanReplacingIt()
    {
        var (context, session) = CreateContext();
        session.PremiumService = WarPremium;
        session.PremiumExpiry = DateTime.UtcNow.AddDays(5);

        context.Character.GivePremium(0, WarPremium, 30).Should().BeTrue();

        session.PremiumExpiry.Should().BeCloseTo(DateTime.UtcNow.AddDays(35), TimeSpan.FromMinutes(1));
        session.PremiumType.Should().Be(WarPremium);
        session.AccountStatus.Should().Be(UserSession.AccountStatusPremium);
    }

    [Fact]
    public void PremiumOfADifferentTypeStartsFromNow()
    {
        var (context, session) = CreateContext();
        session.PremiumService = 11;
        session.PremiumExpiry = DateTime.UtcNow.AddDays(5);

        context.Character.GivePremium(0, WarPremium, 7).Should().BeTrue();

        session.PremiumExpiry.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
        session.PremiumType.Should().Be(WarPremium);
    }

    [Fact]
    public void APremiumGrantOfNoDaysChangesNothing()
    {
        var (context, session) = CreateContext();

        context.Character.GivePremium(0, WarPremium, 0).Should().BeFalse();

        session.PremiumExpiry.Should().BeNull();
        session.PremiumType.Should().Be(0);
    }

    [Fact]
    public void AGrantedPremiumIsWrittenBackToTheAccount()
    {
        var (context, session) = CreateContext();
        context.Character.GivePremium(0, WarPremium, 3).Should().BeTrue();

        var account = new Common.Domain.Entities.Account();
        new UserSessionCharacterMapper().ApplyToAccount(session, account);

        account.PremiumType.Should().Be(WarPremium);
        account.PremiumDate.Should().Be(session.PremiumExpiry);
    }

    private static (QuestScriptContext Context, UserSession Session) CreateContext()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);
        var context = new QuestScriptContext(session, npc: null, Substitute.For<IGameDataService>(),
            sessionManager, Substitute.For<ILogger>(), expMultiplier: 1);
        return (context, session);
    }
}
