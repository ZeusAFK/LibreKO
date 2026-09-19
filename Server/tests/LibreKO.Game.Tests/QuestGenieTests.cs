using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
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

public class QuestGenieTests
{
    private const int SpiritPotion = 700091000;

    [Fact]
    public void AGenieExchangeReachesTheHostWithTheItemAndTheHours()
    {
        var result = QuestCompilation.Create($"""
            Bind Npc 100

            On greeting
                Exchange {SpiritPotion} for 360 hours of genie
            """, "genie.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetGreeting(out var entry).Should().BeTrue();

        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();

        host.Received().RunGenieExchange(SpiritPotion, 360);
    }

    [Fact]
    public void TheExchangeEatsOnePotionAndExtendsTheTimer()
    {
        var (context, session) = CreateContext();
        GiveSlot(session, SpiritPotion, 2);

        context.Items.GenieExchange(0, SpiritPotion, 24).Should().BeTrue();

        session.GenieExpiry.Should().BeCloseTo(DateTime.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
        session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(1);
    }

    [Fact]
    public void ASecondExchangeAddsToWhatIsLeftRatherThanRestarting()
    {
        var (context, session) = CreateContext();
        GiveSlot(session, SpiritPotion, 2);
        session.GenieExpiry = DateTime.UtcNow.AddHours(10);

        context.Items.GenieExchange(0, SpiritPotion, 24).Should().BeTrue();

        session.GenieExpiry.Should().BeCloseTo(DateTime.UtcNow.AddHours(34), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void WithoutThePotionNothingChanges()
    {
        var (context, session) = CreateContext();

        context.Items.GenieExchange(0, SpiritPotion, 24).Should().BeFalse();

        session.GenieExpiry.Should().BeNull();
    }

    [Fact]
    public void RemainingTimeIsCountedInHoursAndNeverRoundsDownToNothing()
    {
        Character.RemainingGenieHours(null).Should().Be(0);
        Character.RemainingGenieHours(DateTime.UtcNow.AddHours(-1)).Should().Be(0);
        Character.RemainingGenieHours(DateTime.UtcNow.AddMinutes(5)).Should().Be(1);
        Character.RemainingGenieHours(DateTime.UtcNow.AddHours(36)).Should().Be(36);
    }

    [Fact]
    public void TheTimerIsWrittenBackToTheCharacter()
    {
        var (context, session) = CreateContext();
        GiveSlot(session, SpiritPotion, 1);
        context.Items.GenieExchange(0, SpiritPotion, 12).Should().BeTrue();

        var character = new Character();
        new UserSessionCharacterMapper().ApplyToCharacter(session, character);

        character.GenieExpiry.Should().Be(session.GenieExpiry);
        character.GenieHours.Should().Be(12);
    }

    private static void GiveSlot(UserSession session, int itemId, ushort count)
    {
        var slot = session.Inventory[InventoryConstants.InventoryStart];
        slot.ItemId = itemId;
        slot.Count = count;
    }

    private static (QuestScriptContext Context, UserSession Session) CreateContext()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);
        var gameData = Substitute.For<IGameDataService>();
        gameData.GetItem(SpiritPotion).Returns(new ItemData { Num = SpiritPotion, Countable = 1 });
        var context = new QuestScriptContext(session, npc: null, gameData,
            sessionManager, Substitute.For<ILogger>(), expMultiplier: 1);
        return (context, session);
    }
}
