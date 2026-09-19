using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Localization;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestDailyTests
{
    private const short QuestId = 1755;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 13, 23, 59, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    [Fact]
    public void ADailyLeavesTheNpcListOnceClaimedAndComesBackWhenItsDayRolls()
    {
        var compilation = QuestCompilation.CreateFromFile(BakedQuestPath("32557_71_1755.quest"));
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("caesar", 32557, 71, [compilation.Program]);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        session.ZoneId = 71;
        session.Level = 61;
        session.Nation = AccountNation.Karus;
        var context = new QuestScriptContext(session, null, Substitute.For<IGameDataService>(), sessions,
            Substitute.For<ILogger>(), 1);
        var definitions = Substitute.For<IQuestDefinitionSource>();
        definitions.TextFor(QuestId).Returns(new QuestText(QuestId, "Elite Pirates", "Hunt one pirate", true));
        var clock = new Clock();
        var host = new QuestScriptHost(session, context, QuestTranslations.Empty,
            Substitute.For<ILogger>(), "daily.quest", definitions, clock);

        bool Listed()
        {
            context.QueuedPackets.Clear();
            Array.Fill(session.Quest.SelectMessageEvents, -1);
            program.TryGetGreeting(out var entry).Should().BeTrue();
            new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
            var offered = context.QueuedPackets.Count;
            context.QueuedPackets.Clear();
            return offered > 0 && session.Quest.SelectMessageEvents[0] >= 0;
        }

        Listed().Should().BeTrue();
        host.SetQuestState(QuestId, 1);
        Listed().Should().BeTrue();
        host.SetQuestState(QuestId, 2);
        Listed().Should().BeFalse();
        clock.Now = clock.Now.AddMinutes(2);
        Listed().Should().BeTrue();
    }

    [Fact]
    public void DailyIsScriptMetadataAndSurvivesComposition()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 32557 Zone 71
            Quest 1755 "Elite Pirates"
                Daily
                Kill 1 of 10800, 10801
            On accept
                Start quest
            """, "daily.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var composed = QuestProgramComposer.Compose("combined", 32557, 71, [compilation.Program]);
        composed.Texts.Single().Daily.Should().BeTrue();
    }

    [Fact]
    public void RepeatAlwaysIsScriptMetadataAndExcludesDaily()
    {
        var compilation = QuestCompilation.Create("""
            Bind Npc 24427 Zone 1
            Quest 401 "[Repeatable Solo] Shadow Seeker Hunt"
                Repeat always
                Kill 5 of 553
            On accept
                Start quest
            """, "repeat.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var composed = QuestProgramComposer.Compose("combined", 24427, 1, [compilation.Program]);
        composed.Texts.Single().Repeat.Should().BeTrue();
        composed.Texts.Single().Daily.Should().BeFalse();

        QuestCompilation.Create("""
            Bind Npc 24427 Zone 1
            Quest 401 "Shadow Seeker Hunt"
                Daily
                Repeat always
                Kill 5 of 553
            """, "both.quest").Succeeded.Should().BeFalse();
        QuestCompilation.Create("""
            Bind Npc 24427 Zone 1
            Quest 401 "Shadow Seeker Hunt"
                Repeat daily
                Kill 5 of 553
            """, "wrong.quest").Succeeded.Should().BeFalse();
    }

    [Fact]
    public void ARepeatableQuestIsUnstartedAndListedAgainTheMomentItIsClaimed()
    {
        const short questId = 401;
        var compilation = QuestCompilation.Create("""
            Bind Npc 24427 Zone 1
            Quest 401 "[Repeatable Solo] Shadow Seeker Hunt"
                Repeat always
                Journal "Hunt five Shadow Seekers."
                Kill 5 of 553

            Requires player level >= 35

            Rewards
                Give 1000 experience

            On offer
                Show quest "Hunt them."

            On claimable
                Show quest "Well done."
            """, "repeat.quest");
        compilation.Succeeded.Should().BeTrue(compilation.RenderDiagnostics());
        var program = QuestProgramComposer.Compose("melverick", 24427, 1, [compilation.Program]);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        session.ZoneId = 1;
        session.Level = 35;
        session.Nation = AccountNation.Karus;
        var context = new QuestScriptContext(session, null, Substitute.For<IGameDataService>(), sessions,
            Substitute.For<ILogger>(), 1);
        var definitions = Substitute.For<IQuestDefinitionSource>();
        definitions.TextFor(questId).Returns(new QuestText(questId, "Shadow Seeker Hunt", "Hunt five", Repeat: true));
        var host = new QuestScriptHost(session, context, QuestTranslations.Empty,
            Substitute.For<ILogger>(), "repeat.quest", definitions, new Clock());

        bool Listed()
        {
            context.QueuedPackets.Clear();
            Array.Fill(session.Quest.SelectMessageEvents, -1);
            program.TryGetGreeting(out var entry).Should().BeTrue();
            new QuestInterpreter(program, host).Run(entry).Failure.Should().BeNull();
            var offered = context.QueuedPackets.Count;
            context.QueuedPackets.Clear();
            return offered > 0 && session.Quest.SelectMessageEvents[0] >= 0;
        }

        Listed().Should().BeTrue();
        host.SetQuestState(questId, 1);
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.Active);
        host.SetQuestState(questId, 2);
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.NotStarted);
        Listed().Should().BeTrue();
    }

    [Theory]
    [InlineData(1755, 32557, AccountNation.Karus, 10800)]
    [InlineData(1756, 32558, AccountNation.ElMorad, 10801)]
    public async Task RetailBakedHuntPaysOncePerCharacterDayAndRejectsPrematureOrReplayedClaims(
        short questId, short npcId, AccountNation nation, int monster)
    {
        using var harness = new RetailHarness(questId, npcId, nation);
        var session = harness.Session;
        session.Nation = nation == AccountNation.Karus ? AccountNation.ElMorad : AccountNation.Karus;
        await harness.Request(QuestSubOpcode.Accept);
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.NotStarted);
        session.Nation = nation;
        await harness.Request(QuestSubOpcode.Accept);
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.Active);
        await harness.Request(QuestSubOpcode.CheckFulfill);
        session.Quest.QuestMap[questId] = 3;
        await harness.RunFulfilDirectly();
        session.Loyalty.Should().Be(0);
        session.Experience.Should().Be(0);
        session.Quest.QuestMap[questId] = 1;
        await harness.Progression.CheckQuestKillAsync(session, 2815);
        session.Quest.GetQuestKillCounts(questId)[0].Should().Be(0);
        await harness.Progression.CheckQuestKillAsync(session, monster);
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.ReadyToTurnIn);

        session.Loyalty = int.MaxValue;
        await harness.Request(QuestSubOpcode.CheckFulfill);
        session.Experience.Should().Be(0);
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.ReadyToTurnIn);
        session.Quest.DailyCompletionDays.Should().BeEmpty();
        session.Loyalty = 0;
        if (questId == 1755)
            await harness.ClaimThroughNpcTopic();
        else
            await harness.Request(QuestSubOpcode.CheckFulfill);
        session.Loyalty.Should().Be(100);
        session.Experience.Should().Be(1_000_000);
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.Completed);
        session.LoadQuestData(session.SerializeQuestData());
        await harness.Request(QuestSubOpcode.Accept);
        await harness.RunFulfilDirectly();
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.Completed);
        session.Loyalty.Should().Be(100);
        session.Experience.Should().Be(1_000_000);

        harness.Time.Now = harness.Time.Now.AddMinutes(2);
        await harness.Request(QuestSubOpcode.Accept);
        session.Quest.StatusOf(questId).Should().Be(QuestStatus.Active);
        session.Quest.GetQuestKillCounts(questId)[0].Should().Be(0);
        await harness.Progression.CheckQuestKillAsync(session, monster);
        await harness.Request(QuestSubOpcode.CheckFulfill);
        session.Loyalty.Should().Be(200);
        session.Experience.Should().Be(2_000_000);
        harness.Data.DidNotReceiveWithAnyArgs().GetItemExchange(default);
    }

    [Fact]
    public async Task ConcurrentClaimsPayOnlyOnce()
    {
        using var harness = new RetailHarness(1755, 32557, AccountNation.Karus);
        await harness.Request(QuestSubOpcode.Accept);
        await harness.Progression.CheckQuestKillAsync(harness.Session, 10800);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(harness.RunFulfilDirectly)));
        harness.Session.Loyalty.Should().Be(100);
        harness.Session.Experience.Should().Be(1_000_000);
        harness.Session.Quest.StatusOf(1755).Should().Be(QuestStatus.Completed);
    }

    private sealed class RetailHarness : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "quest-retail-" + Guid.NewGuid());
        private readonly IClient _client = Substitute.For<IClient>();
        private readonly short _questId;
        private readonly NpcInstance _npc;
        private readonly QuestScriptEngine _engine;
        public IGameDataService Data { get; } = Substitute.For<IGameDataService>();
        public Clock Time { get; } = new();
        public UserSession Session { get; }
        public QuestProgressionService Progression { get; }

        public RetailHarness(short questId, short npcId, AccountNation nation,
            [System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            _questId = questId;
            Directory.CreateDirectory(_directory);
            var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", ".."));
            var file = $"{npcId}_71_{questId}.quest";
            File.Copy(Path.Combine(root, "LibreKO.Game", "Quests", file), Path.Combine(_directory, file));
            var sessions = new SessionManager();
            _client.Id.Returns(Guid.NewGuid());
            Session = sessions.CreateSession(_client, 1, 1);
            Session.Level = 61;
            Session.Class = nation == AccountNation.Karus ? (short)101 : (short)201;
            Session.Nation = nation;
            Session.Hp = 100;
            Session.ZoneId = 71;
            _npc = sessions.Regions.SpawnNpc(new NpcInstance { NpcId = npcId, ZoneId = 71, Hp = 100, MaxHp = 100 });
            Session.Quest.EventNpcId = npcId;
            Session.Quest.EventNpcUniqueId = _npc.UniqueId;
            _engine = new QuestScriptEngine(Data, sessions, new ForwardingEffectApplier(), QuestTranslations.Empty,
                TestHostEnvironmentFactory.Create(_directory), Options.Create(new GameServerSettings { QuestsDirectory = _directory }),
                Substitute.For<ILogger<QuestScriptEngine>>(), Time);
            var runner = new QuestDialogRunner(_engine, Substitute.For<ILogger<QuestDialogRunner>>());
            Progression = new QuestProgressionService(sessions, Data, runner, _engine,
                Substitute.For<ICharacterStatePersister>(), Substitute.For<ILogger<QuestProgressionService>>(), clock: Time);
        }

        public Task Request(QuestSubOpcode operation)
        {
            var packet = new Packet(GameOpcodes.GS_QUEST);
            packet.WriteByte((byte)operation);
            packet.WriteInt(_questId);
            return Progression.HandleQuestAsync(_client, packet);
        }

        public async Task RunFulfilDirectly()
        {
            _engine.TryGetEntry(_npc.NpcId, 71, QuestProgram.FulfilEvent, _questId, out var file, out var entry)
                .Should().BeTrue();
            (await _engine.ExecuteAsync(Session, _npc, entry, -1, file)).Should().BeTrue();
        }

        public async Task ClaimThroughNpcTopic()
        {
            _engine.TryGetGreeting(_npc.NpcId, 71, out var file, out var greeting).Should().BeTrue();
            (await _engine.ExecuteAsync(Session, _npc, greeting, -1, file)).Should().BeTrue();
            for (var click = 0; click < 1; click++)
            {
                var entry = Session.Quest.SelectMessageEvents[0];
                entry.Should().BeGreaterThan(0);
                (await _engine.ExecuteAsync(Session, _npc, entry, -1, file)).Should().BeTrue();
            }
            Session.Quest.StatusOf(_questId).Should().Be(QuestStatus.ReadyToTurnIn);
            await Request(QuestSubOpcode.CheckFulfill);
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletionSurvivesReloadAndOnlyDailyQuestsReopenAtUtcMidnight(bool daily)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        var context = new QuestScriptContext(session, null, Substitute.For<IGameDataService>(), sessions,
            Substitute.For<ILogger>(), 1);
        var definitions = Substitute.For<IQuestDefinitionSource>();
        definitions.TextFor(QuestId).Returns(new QuestText(QuestId, "Elite Pirates", "Hunt one pirate", daily));
        var clock = new Clock();
        var host = new QuestScriptHost(session, context, QuestTranslations.Empty,
            Substitute.For<ILogger>(), "daily.quest", definitions, clock);
        host.SetQuestState(QuestId, 1);
        host.SetQuestState(QuestId, 2);
        var saved = session.SerializeQuestData();
        session.Quest.DailyCompletionDays.Clear();
        session.LoadQuestData(saved);
        host.QuestStatus(QuestId).Should().Be(2);
        clock.Now = clock.Now.AddMinutes(2);
        host.QuestStatus(QuestId).Should().Be(daily ? 0 : 2);
        if (daily)
        {
            host.SetQuestState(QuestId, 1);
            host.SetQuestState(QuestId, 2);
            host.QuestStatus(QuestId).Should().Be(2);
        }
    }

    [Fact]
    public void APreviouslyCompletedQuestWithoutADateWaitsUntilTomorrow()
    {
        var state = new QuestState();
        var today = new DateOnly(2026, 9, 13);
        state.QuestMap[QuestId] = 2;
        state.RefreshDailyQuest(QuestId, today).Should().BeTrue();
        state.StatusOf(QuestId).Should().Be(QuestStatus.Completed);
        state.RefreshDailyQuest(QuestId, today).Should().BeFalse();
        state.RefreshDailyQuest(QuestId, today.AddDays(1)).Should().BeTrue();
        state.StatusOf(QuestId).Should().Be(QuestStatus.NotStarted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ActiveProgressSurvivesTheDayBoundary(byte status)
    {
        var state = new QuestState();
        state.QuestMap[QuestId] = status;
        state.GetOrCreateQuestKillCounts(QuestId)[0] = 1;
        state.RefreshDailyQuest(QuestId, new DateOnly(2026, 9, 14)).Should().BeFalse();
        state.GetQuestKillCounts(QuestId)[0].Should().Be(1);
        state.QuestMap[QuestId].Should().Be(status);
    }

    [Fact]
    public void OldQuestBlobsAndMalformedDailyTrailersKeepCompletedQuestsClosed()
    {
        var state = new QuestState();
        state.QuestMap[QuestId] = 2;
        state.DailyCompletionDays[QuestId] = new DateOnly(2026, 9, 13).DayNumber;
        var old = UserSessionBinaryState.SerializeQuestData(state.QuestMap, state.QuestKillCountsMap);
        var current = UserSessionBinaryState.SerializeQuestData(state.QuestMap, state.QuestKillCountsMap,
            state.DailyCompletionDays);
        foreach (var bytes in new[] { old, current[..^1], new byte[] { 1, 0, 219, 6, 2 } })
        {
            UserSessionBinaryState.LoadQuestData(state.QuestMap, state.QuestKillCountsMap, bytes,
                state.DailyCompletionDays);
            state.StatusOf(QuestId).Should().Be(QuestStatus.Completed);
            state.DailyCompletionDays.Should().BeEmpty();
        }
    }
}
