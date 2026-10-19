using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestScriptProtocolTests : GameTestBase
{
    private readonly SessionManager sessions = new();
    private readonly IGameDataService data = Substitute.For<IGameDataService>();
    private readonly IQuestDialogRunner runner = Substitute.For<IQuestDialogRunner>();
    private readonly IQuestDefinitionSource objectives = Substitute.For<IQuestDefinitionSource>();
    private readonly IClient client = Substitute.For<IClient>();
    private readonly UserSession session;
    private readonly NpcInstance npc;
    private readonly List<Packet> sent = [];
    private readonly QuestProgressionService service;

    public QuestScriptProtocolTests()
    {
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            sent.Add(ClonePacket(call.Arg<Packet>()));
            return Task.CompletedTask;
        });
        session = sessions.CreateSession(client, 1, 1);
        session.Hp = 100;
        session.ZoneId = 1;
        npc = sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 100, ZoneId = 1, Hp = 100, MaxHp = 100,
        });
        session.Quest.EventNpcId = npc.NpcId;
        session.Quest.EventNpcUniqueId = npc.UniqueId;
        service = new QuestProgressionService(sessions, data, runner, objectives,
            Substitute.For<ICharacterStatePersister>(),
            Substitute.For<ILogger<QuestProgressionService>>());
    }

    [Fact]
    public async Task AvailabilityRefreshWaitsForTheJournalAfterEnteringEachZone()
    {
        var refresh = new QuestAvailabilityService(sessions, objectives, Substitute.For<ILogger<QuestAvailabilityService>>());
        await refresh.ProcessTickAsync();
        await objectives.DidNotReceiveWithAnyArgs().SendViewsAsync(default!, default, default);
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.QuestList));
        await refresh.ProcessTickAsync();
        await objectives.Received(1).SendViewsAsync(session, changesOnly: true);
        session.ZoneId = 21;
        await refresh.ProcessTickAsync();
        await objectives.Received(1).SendViewsAsync(session, changesOnly: true);
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.QuestList));
        await refresh.ProcessTickAsync();
        await objectives.Received(2).SendViewsAsync(session, changesOnly: true);
        session.Hp = 0;
        await refresh.ProcessTickAsync();
        await objectives.Received(2).SendViewsAsync(session, changesOnly: true);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public async Task NotificationRepliesUseTheirOwnChannelAndRejectTrailingData(short choice, bool trailing)
    {
        session.Quest.EventNpcId = 0;
        session.Quest.EventNpcUniqueId = 0;
        var packet = Request(QuestSubOpcode.NotificationReply, 62);
        packet.WriteShort(choice);
        if (trailing) packet.WriteByte(0);
        packet.ResetOffset();
        await service.HandleQuestAsync(client, packet);
        await objectives.Received(trailing ? 0 : 1).ReplyToNotificationAsync(session, 62, choice);
        await runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default, default, default!);
    }

    private static Packet Request(QuestSubOpcode sub, int quest = 777)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte((byte)sub);
        packet.WriteInt(quest);
        packet.ResetOffset();
        return packet;
    }

    [Theory]
    [InlineData(QuestSubOpcode.Accept, QuestProgram.AcceptEvent, 0)]
    [InlineData(QuestSubOpcode.CheckFulfill, QuestProgram.FulfilEvent, 3)]
    [InlineData(QuestSubOpcode.Abandon, QuestProgram.AbandonEvent, 1)]
    public async Task QuestActionsAddressAnEntryWithoutAHelperRow(QuestSubOpcode sub, string role, byte state)
    {
        session.Quest.QuestMap[777] = state;
        runner.TryEntryAsync(session, npc, role, 777).Returns(true);
        await service.HandleQuestAsync(client, Request(sub));
        await runner.Received(1).TryEntryAsync(session, npc, role, 777);
    }

    [Fact]
    public async Task AClientCannotWriteQuestStateOrFulfilAnUnfinishedHunt()
    {
        session.Quest.QuestMap[777] = 1;
        var forged = new Packet(GameOpcodes.GS_QUEST);
        forged.WriteByte((byte)QuestSubOpcode.StateChange);
        forged.WriteShort(777);
        forged.WriteByte(2);
        await service.HandleQuestAsync(client, forged);
        session.Quest.QuestMap[777].Should().Be(1);
        objectives.ObjectivesFor(777).Returns(new QuestObjectives(777, [new KillObjective(7, [100])]));
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.CheckFulfill));
        await runner.DidNotReceiveWithAnyArgs().TryEntryAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ACompletedQuestCannotBeAcceptedAgainAndADistantNpcCannotFulfilIt()
    {
        session.Quest.QuestMap[777] = 2;
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.Accept));
        session.Quest.QuestMap[777] = 3;
        session.X = 1000;
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.CheckFulfill));
        await runner.DidNotReceiveWithAnyArgs().TryEntryAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task DisabledFallbackHidesUndeclaredJournalEntriesWithoutDeletingSavedProgress()
    {
        objectives.TextFor(62).Returns(new QuestText(62, "Hunt", "Hunt"));
        session.Quest.QuestMap[62] = 1;
        session.Quest.QuestMap[777] = 3;
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.QuestList));
        var list = sent.Single(packet => packet.ReadByte() == (byte)QuestSubOpcode.QuestList);
        list.ReadShort().Should().Be(1);
        list.ReadShort().Should().Be(62);
        list.ReadByte().Should().Be(1);
        session.Quest.QuestMap[777].Should().Be(3);
        await service.CheckQuestKillAsync(session, 750);
    }

    [Fact]
    public async Task AutoAcceptedQuestCannotBeAbandonedByAForgedRequest()
    {
        objectives.IsAutoAccepted(777).Returns(true);
        session.Quest.QuestMap[777] = 1;
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.Abandon));
        session.Quest.QuestMap[777].Should().Be(1);
        await runner.DidNotReceiveWithAnyArgs().TryEntryAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ScriptOnlyKillCountsAreSentOnJournalReload()
    {
        objectives.ObjectivesFor(777).Returns(new QuestObjectives(777, [new KillObjective(7, [100])]));
        session.Quest.QuestMap[777] = 1;
        session.Quest.GetOrCreateQuestKillCounts(777)[0] = 4;
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.QuestList));
        var counts = sent.Single(packet => packet.ReadByte() == (byte)QuestSubOpcode.KillCounts);
        counts.ReadByte().Should().Be(1);
        counts.ReadShort().Should().Be(777);
        counts.ReadUShort().Should().Be(4);
    }

    [Fact]
    public async Task TheJournalReloadNamesEveryQuestTheScriptsDeclare()
    {
        objectives.TextFor(777).Returns(new QuestText(777, "Silk Spool", "Bring the spool back."));
        session.Quest.QuestMap[777] = 1;
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.QuestList));

        var text = sent.Single(packet => packet.ReadByte() == (byte)QuestSubOpcode.Text);
        text.ReadShort().Should().Be(1);
        text.ReadShort().Should().Be(777);
        text.ReadUtf8String().Should().Be("Silk Spool");
        text.ReadUtf8String().Should().Be("Bring the spool back.");
    }

    [Fact]
    public async Task AQuestNoScriptNamesSendsNoText()
    {
        session.Quest.QuestMap[777] = 1;
        await service.HandleQuestAsync(client, Request(QuestSubOpcode.QuestList));

        sent.Should().NotContain(packet => packet.ReadByte() == (byte)QuestSubOpcode.Text);
    }

    [Fact]
    public async Task ADialogReplyUsesTheServersScriptAndCanOnlyBeConsumedOnce()
    {
        session.Quest.ActiveQuestScript = "rewards/friendly.quest";
        session.Quest.SelectMessageEvents[0] = 123;
        var interaction = new QuestNpcInteractionService(sessions, data, runner,
            Substitute.For<IKingSystemRuntimeService>(), Substitute.For<IDrakiTowerService>(), Substitute.For<IDrakiStageProvider>(),
            Substitute.For<ITouchTrapService>(), Substitute.For<IChaosDungeonService>(), Substitute.For<IBifrostEventService>(),
            Substitute.For<ILogger<QuestNpcInteractionService>>(),
            Substitute.For<IFullMoonRiftService>(), Substitute.For<IItemGrantService>(), Substitute.For<IMagicItemUsageService>());
        Packet Click()
        {
            var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
            packet.WriteByte(0);
            packet.WriteSByteString("another.quest");
            packet.WriteByte(255);
            return packet;
        }
        await interaction.HandleSelectMsgAsync(client, Click());
        await interaction.HandleSelectMsgAsync(client, Click());
        await runner.Received(1).RunAsync(session, npc, 123, -1, "rewards/friendly.quest");
        await runner.DidNotReceive().RunAsync(session, npc, 123, -1, "another.quest");
    }

    [Theory]
    [InlineData(255, 1)]
    [InlineData(4, 1)]
    [InlineData(4, 19)]
    [InlineData(4, 255)]
    public async Task ScriptRewardChoicesUseTheOfferedRewardInsteadOfTheClientIndex(byte claimedReward, byte menuIndex)
    {
        session.Quest.ActiveQuestScript = "choices.quest";
        session.Quest.IsScriptDialog = true;
        session.Quest.SelectMessageEvents[menuIndex] = 123;
        session.Quest.SelectMessageRewards[menuIndex] = 2;
        var interaction = new QuestNpcInteractionService(sessions, data, runner,
            Substitute.For<IKingSystemRuntimeService>(), Substitute.For<IDrakiTowerService>(), Substitute.For<IDrakiStageProvider>(),
            Substitute.For<ITouchTrapService>(), Substitute.For<IChaosDungeonService>(), Substitute.For<IBifrostEventService>(),
            Substitute.For<ILogger<QuestNpcInteractionService>>(),
            Substitute.For<IFullMoonRiftService>(), Substitute.For<IItemGrantService>(), Substitute.For<IMagicItemUsageService>());
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteByte(menuIndex);
        packet.WriteSByteString("choices.quest");
        packet.WriteByte(claimedReward);

        await interaction.HandleSelectMsgAsync(client, packet);

        await runner.Received(1).RunAsync(session, npc, 123, 2, "choices.quest");
        session.Quest.SelectMessageRewards.Should().OnlyContain(reward => reward == -1);
    }

    [Fact]
    public async Task CancellingARewardDialogCannotExecuteItsFirstButton()
    {
        session.Quest.ActiveQuestScript = "reward.quest";
        session.Quest.SelectMessageFlag = 5;
        session.Quest.SelectMessageEvents[0] = 123;
        session.Quest.SelectMessageEvents[1] = -1;
        var interaction = new QuestNpcInteractionService(sessions, data, runner,
            Substitute.For<IKingSystemRuntimeService>(), Substitute.For<IDrakiTowerService>(), Substitute.For<IDrakiStageProvider>(),
            Substitute.For<ITouchTrapService>(), Substitute.For<IChaosDungeonService>(), Substitute.For<IBifrostEventService>(),
            Substitute.For<ILogger<QuestNpcInteractionService>>(),
            Substitute.For<IFullMoonRiftService>(), Substitute.For<IItemGrantService>(), Substitute.For<IMagicItemUsageService>());
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteByte(1);
        packet.WriteSByteString("reward.quest");
        packet.WriteByte(255);

        await interaction.HandleSelectMsgAsync(client, packet);

        await runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default, default, default!);
    }

    [Fact]
    public async Task AZoneScopedObjectiveCountsOnlyKillsMadeInsideThatZone()
    {
        objectives.ObjectivesFor(777).Returns(new QuestObjectives(777, [new KillObjective(2, [1], Zone: 71)]));
        session.Quest.QuestMap[777] = 1;

        session.ZoneId = 72;
        await service.CheckQuestKillAsync(session, 1);
        session.Quest.GetOrCreateQuestKillCounts(777)[0].Should().Be(0);
        session.Quest.QuestMap[777].Should().Be(1);

        session.ZoneId = 71;
        await service.CheckQuestKillAsync(session, 1);
        session.Quest.GetOrCreateQuestKillCounts(777)[0].Should().Be(1);
        await service.CheckQuestKillAsync(session, 1);
        session.Quest.QuestMap[777].Should().Be(3);
    }
}
