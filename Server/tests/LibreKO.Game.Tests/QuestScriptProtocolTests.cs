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
    private readonly SessionManager _sessions = new();
    private readonly IGameDataService _data = Substitute.For<IGameDataService>();
    private readonly IQuestDialogRunner _runner = Substitute.For<IQuestDialogRunner>();
    private readonly IQuestDefinitionSource _objectives = Substitute.For<IQuestDefinitionSource>();
    private readonly IClient _client = Substitute.For<IClient>();
    private readonly UserSession _session;
    private readonly NpcInstance _npc;
    private readonly List<Packet> _sent = [];
    private readonly QuestProgressionService _service;

    public QuestScriptProtocolTests()
    {
        _client.Id.Returns(Guid.NewGuid());
        _client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _sent.Add(ClonePacket(call.Arg<Packet>()));
            return Task.CompletedTask;
        });
        _session = _sessions.CreateSession(_client, 1, 1);
        _session.Hp = 100;
        _session.ZoneId = 1;
        _npc = _sessions.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = 100, ZoneId = 1, Hp = 100, MaxHp = 100,
        });
        _session.Quest.EventNpcId = _npc.NpcId;
        _session.Quest.EventNpcUniqueId = _npc.UniqueId;
        _service = new QuestProgressionService(_sessions, _data, _runner, _objectives,
            Substitute.For<ICharacterStatePersister>(),
            Substitute.For<ILogger<QuestProgressionService>>());
    }

    [Fact]
    public async Task AvailabilityRefreshWaitsForTheJournalAfterEnteringEachZone()
    {
        var refresh = new QuestAvailabilityService(_sessions, _objectives, Substitute.For<ILogger<QuestAvailabilityService>>());
        await refresh.ProcessTickAsync();
        await _objectives.DidNotReceiveWithAnyArgs().SendViewsAsync(default!, default, default);
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.QuestList));
        await refresh.ProcessTickAsync();
        await _objectives.Received(1).SendViewsAsync(_session, changesOnly: true);
        _session.ZoneId = 21;
        await refresh.ProcessTickAsync();
        await _objectives.Received(1).SendViewsAsync(_session, changesOnly: true);
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.QuestList));
        await refresh.ProcessTickAsync();
        await _objectives.Received(2).SendViewsAsync(_session, changesOnly: true);
        _session.Hp = 0;
        await refresh.ProcessTickAsync();
        await _objectives.Received(2).SendViewsAsync(_session, changesOnly: true);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public async Task NotificationRepliesUseTheirOwnChannelAndRejectTrailingData(short choice, bool trailing)
    {
        _session.Quest.EventNpcId = 0;
        _session.Quest.EventNpcUniqueId = 0;
        var packet = Request(QuestSubOpcode.NotificationReply, 62);
        packet.WriteShort(choice);
        if (trailing) packet.WriteByte(0);
        packet.ResetOffset();
        await _service.HandleQuestAsync(_client, packet);
        await _objectives.Received(trailing ? 0 : 1).ReplyToNotificationAsync(_session, 62, choice);
        await _runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default, default, default!);
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
        _session.Quest.QuestMap[777] = state;
        _runner.TryEntryAsync(_session, _npc, role, 777).Returns(true);
        await _service.HandleQuestAsync(_client, Request(sub));
        await _runner.Received(1).TryEntryAsync(_session, _npc, role, 777);
    }

    [Fact]
    public async Task AClientCannotWriteQuestStateOrFulfilAnUnfinishedHunt()
    {
        _session.Quest.QuestMap[777] = 1;
        var forged = new Packet(GameOpcodes.GS_QUEST);
        forged.WriteByte((byte)QuestSubOpcode.StateChange);
        forged.WriteShort(777);
        forged.WriteByte(2);
        await _service.HandleQuestAsync(_client, forged);
        _session.Quest.QuestMap[777].Should().Be(1);
        _objectives.ObjectivesFor(777).Returns(new QuestObjectives(777, [new KillObjective(7, [100])]));
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.CheckFulfill));
        await _runner.DidNotReceiveWithAnyArgs().TryEntryAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ACompletedQuestCannotBeAcceptedAgainAndADistantNpcCannotFulfilIt()
    {
        _session.Quest.QuestMap[777] = 2;
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.Accept));
        _session.Quest.QuestMap[777] = 3;
        _session.X = 1000;
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.CheckFulfill));
        await _runner.DidNotReceiveWithAnyArgs().TryEntryAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task DisabledFallbackHidesUndeclaredJournalEntriesWithoutDeletingSavedProgress()
    {
        _objectives.TextFor(62).Returns(new QuestText(62, "Hunt", "Hunt"));
        _session.Quest.QuestMap[62] = 1;
        _session.Quest.QuestMap[777] = 3;
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.QuestList));
        var list = _sent.Single(packet => packet.ReadByte() == (byte)QuestSubOpcode.QuestList);
        list.ReadShort().Should().Be(1);
        list.ReadShort().Should().Be(62);
        list.ReadByte().Should().Be(1);
        _session.Quest.QuestMap[777].Should().Be(3);
        await _service.CheckQuestKillAsync(_session, 750);
    }

    [Fact]
    public async Task AutoAcceptedQuestCannotBeAbandonedByAForgedRequest()
    {
        _objectives.IsAutoAccepted(777).Returns(true);
        _session.Quest.QuestMap[777] = 1;
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.Abandon));
        _session.Quest.QuestMap[777].Should().Be(1);
        await _runner.DidNotReceiveWithAnyArgs().TryEntryAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ScriptOnlyKillCountsAreSentOnJournalReload()
    {
        _objectives.ObjectivesFor(777).Returns(new QuestObjectives(777, [new KillObjective(7, [100])]));
        _session.Quest.QuestMap[777] = 1;
        _session.Quest.GetOrCreateQuestKillCounts(777)[0] = 4;
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.QuestList));
        var counts = _sent.Single(packet => packet.ReadByte() == (byte)QuestSubOpcode.KillCounts);
        counts.ReadByte().Should().Be(1);
        counts.ReadShort().Should().Be(777);
        counts.ReadUShort().Should().Be(4);
    }

    [Fact]
    public async Task TheJournalReloadNamesEveryQuestTheScriptsDeclare()
    {
        _objectives.TextFor(777).Returns(new QuestText(777, "Silk Spool", "Bring the spool back."));
        _session.Quest.QuestMap[777] = 1;
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.QuestList));

        var text = _sent.Single(packet => packet.ReadByte() == (byte)QuestSubOpcode.Text);
        text.ReadShort().Should().Be(1);
        text.ReadShort().Should().Be(777);
        text.ReadUtf8String().Should().Be("Silk Spool");
        text.ReadUtf8String().Should().Be("Bring the spool back.");
    }

    [Fact]
    public async Task AQuestNoScriptNamesSendsNoText()
    {
        _session.Quest.QuestMap[777] = 1;
        await _service.HandleQuestAsync(_client, Request(QuestSubOpcode.QuestList));

        _sent.Should().NotContain(packet => packet.ReadByte() == (byte)QuestSubOpcode.Text);
    }

    [Fact]
    public async Task ADialogReplyUsesTheServersScriptAndCanOnlyBeConsumedOnce()
    {
        _session.Quest.ActiveQuestScript = "rewards/friendly.quest";
        _session.Quest.SelectMessageEvents[0] = 123;
        var interaction = new QuestNpcInteractionService(_sessions, _data, _runner,
            Substitute.For<ILogger<QuestNpcInteractionService>>());
        Packet Click()
        {
            var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
            packet.WriteByte(0);
            packet.WriteSByteString("another.quest");
            packet.WriteByte(255);
            return packet;
        }
        await interaction.HandleSelectMsgAsync(_client, Click());
        await interaction.HandleSelectMsgAsync(_client, Click());
        await _runner.Received(1).RunAsync(_session, _npc, 123, -1, "rewards/friendly.quest");
        await _runner.DidNotReceive().RunAsync(_session, _npc, 123, -1, "another.quest");
    }

    [Theory]
    [InlineData(255, 1)]
    [InlineData(4, 1)]
    [InlineData(4, 19)]
    [InlineData(4, 255)]
    public async Task ScriptRewardChoicesUseTheOfferedRewardInsteadOfTheClientIndex(byte claimedReward, byte menuIndex)
    {
        _session.Quest.ActiveQuestScript = "choices.quest";
        _session.Quest.IsScriptDialog = true;
        _session.Quest.SelectMessageEvents[menuIndex] = 123;
        _session.Quest.SelectMessageRewards[menuIndex] = 2;
        var interaction = new QuestNpcInteractionService(_sessions, _data, _runner,
            Substitute.For<ILogger<QuestNpcInteractionService>>());
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteByte(menuIndex);
        packet.WriteSByteString("choices.quest");
        packet.WriteByte(claimedReward);

        await interaction.HandleSelectMsgAsync(_client, packet);

        await _runner.Received(1).RunAsync(_session, _npc, 123, 2, "choices.quest");
        _session.Quest.SelectMessageRewards.Should().OnlyContain(reward => reward == -1);
    }

    [Fact]
    public async Task CancellingARewardDialogCannotExecuteItsFirstButton()
    {
        _session.Quest.ActiveQuestScript = "reward.quest";
        _session.Quest.SelectMessageFlag = 5;
        _session.Quest.SelectMessageEvents[0] = 123;
        _session.Quest.SelectMessageEvents[1] = -1;
        var interaction = new QuestNpcInteractionService(_sessions, _data, _runner,
            Substitute.For<ILogger<QuestNpcInteractionService>>());
        var packet = new Packet(GameOpcodes.GS_SELECT_MSG);
        packet.WriteByte(1);
        packet.WriteSByteString("reward.quest");
        packet.WriteByte(255);

        await interaction.HandleSelectMsgAsync(_client, packet);

        await _runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default, default, default!);
    }

    [Fact]
    public async Task AZoneScopedObjectiveCountsOnlyKillsMadeInsideThatZone()
    {
        _objectives.ObjectivesFor(777).Returns(new QuestObjectives(777, [new KillObjective(2, [1], Zone: 71)]));
        _session.Quest.QuestMap[777] = 1;

        _session.ZoneId = 72;
        await _service.CheckQuestKillAsync(_session, 1);
        _session.Quest.GetOrCreateQuestKillCounts(777)[0].Should().Be(0);
        _session.Quest.QuestMap[777].Should().Be(1);

        _session.ZoneId = 71;
        await _service.CheckQuestKillAsync(_session, 1);
        _session.Quest.GetOrCreateQuestKillCounts(777)[0].Should().Be(1);
        await _service.CheckQuestKillAsync(_session, 1);
        _session.Quest.QuestMap[777].Should().Be(3);
    }
}
