using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Localization;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestInventoryPredicateTests
{
    private readonly IGameDataService _data = Substitute.For<IGameDataService>();
    private readonly UserSession _session;
    private readonly QuestScriptHost _host;

    public QuestInventoryPredicateTests()
    {
        var sessions = new SessionManager();
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        _session = sessions.CreateSession(client, 1, 1);
        _session.Hp = 100;
        var logger = Substitute.For<ILogger<QuestScriptHost>>();
        var context = new QuestScriptContext(_session, null, _data, sessions, logger, 1);
        _host = new QuestScriptHost(_session, context, QuestTranslations.Empty, logger, "inventory.quest");
    }

    private void Run(string condition)
    {
        var compilation = QuestCompilation.Create($"Bind Npc 100\nOn greeting\n    If {condition}\n        Give 1 coins\n", "inventory.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        compilation.Program.TryGetEntry(QuestProgram.GreetingEvent, 0, out var entry).Should().BeTrue();
        new QuestInterpreter(compilation.Program, _host).Run(entry).Failure.Should().BeNull();
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    public void ItemCapacityUsesTheExistingStackAndRequestedQuantity(int count, int gold)
    {
        _data.GetItem(123).Returns(new ItemData { Num = 123, Countable = 1 });
        for (var slot = InventoryConstants.InventoryStart;
             slot < InventoryConstants.InventoryStart + InventoryConstants.HaveMax; slot++)
        {
            _session.Inventory[slot].ItemId = 456;
            _session.Inventory[slot].Count = 1;
        }
        _session.Inventory[InventoryConstants.InventoryStart].ItemId = 123;
        _session.Inventory[InventoryConstants.InventoryStart].Count = 9997;

        Run($"player can receive {count} of 123");

        _session.Money.Should().Be(gold);
        _session.Inventory[InventoryConstants.InventoryStart].Count.Should().Be(9997);
    }

    [Theory]
    [InlineData(100, 1)]
    [InlineData(0, 0)]
    public void StackCapacityPreservesTheLivePlayerCheck(int hp, int gold)
    {
        _session.Hp = (short)hp;
        Run("player can receive 3 stacks");
        _session.Money.Should().Be(gold);
    }

    [Fact]
    public void WeightChecksDoNotReadTheNumberOfFreeSlots()
    {
        _session.Stats.ItemWeight = 150;
        Run("player weight > 100 and player space > 0");
        _session.Money.Should().Be(1);
    }
}
