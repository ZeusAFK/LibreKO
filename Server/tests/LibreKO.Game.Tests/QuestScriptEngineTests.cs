using FluentAssertions;
using LibreKO.Common.Enums;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;

namespace LibreKO.Game.Tests;

public class QuestScriptEngineTests : GameTestBase, IDisposable
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now = _now.AddSeconds(1);
    }
    private const int NpcId = 16079;
    private const int GreetingEvent = 1205;
    private const int UnhandledEvent = 4242;
    private const string ScriptName = "16079_Test.quest";

    private const string Script = """
        Bind Npc 16079

        greeting_topic = event 1205

        On greeting_topic for quest 61
            Say 9352
            Topic 27 goto close
        """;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "kq-" + Guid.NewGuid().ToString("N"));

    public QuestScriptEngineTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, ScriptName), Script);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AQuestFileReachesAnEventDeclaredAsSharedAndAnsweredByItsNpcsOtherFile()
    {
        File.Delete(Path.Combine(_directory, ScriptName));
        File.WriteAllText(Path.Combine(_directory, "16079_21_0.quest"), """
            Bind Npc 16079 Zone 21
            On greeting
                Say "Hello."
            On payout
                Give 23 coins
            """);
        File.WriteAllText(Path.Combine(_directory, "16079_21_61.quest"), """
            Bind Npc 16079 Zone 21
            payout = event from 16079_21_0
            Quest 61
            On accept
                Goto payout
            """);

        var (engine, session, _) = CreateHarness();
        session.ZoneId = 21;
        engine.TryGetEntry(NpcId, 21, QuestProgram.AcceptEvent, 61, out var file, out var accept)
            .Should().BeTrue();
        await engine.ExecuteAsync(session, null, accept, -1, file);
        session.Money.Should().Be(23);
    }

    [Fact]
    public async Task AnEventEachFileAnswersForItselfIsNotSharedByAccident()
    {
        File.Delete(Path.Combine(_directory, ScriptName));
        foreach (var (quest, coins) in new[] { (61, 5), (62, 9) })
            File.WriteAllText(Path.Combine(_directory, $"16079_21_{quest}.quest"), $"""
                Bind Npc 16079 Zone 21
                Quest {quest}
                On accept
                    Goto payout
                On payout
                    Give {coins} coins
                """);

        var (engine, session, _) = CreateHarness();
        session.ZoneId = 21;
        engine.TryGetEntry(NpcId, 21, QuestProgram.AcceptEvent, 62, out var file, out var accept)
            .Should().BeTrue();
        await engine.ExecuteAsync(session, null, accept, -1, file);
        session.Money.Should().Be(9);
    }

    [Fact]
    public async Task BoundQuestFilesShareTopicsAndKeepRepliesAndQuestScopesSeparate()
    {
        File.Delete(Path.Combine(_directory, ScriptName));
        foreach (var (quest, coins) in new[] { (61, 7), (62, 11) })
            File.WriteAllText(Path.Combine(_directory, $"16079_21_{quest}.quest"), $"""
                Bind Npc 16079 Zone 21
                Quest {quest}
                On greeting
                    Say "Hello from {quest}."
                On topics
                    If quest is available
                        Topic "Quest {quest}" goto collect
                On accept
                    Start quest
                On collect
                    Give {coins} coins
                    Complete quest
                """);
        var (engine, session, sent) = CreateHarness();
        session.ZoneId = 21;
        engine.TryGetGreeting(NpcId, 22, out _, out _).Should().BeFalse();
        engine.TryGetGreeting(NpcId, 21, out var file, out var greeting).Should().BeTrue();
        await engine.ExecuteAsync(session, null, greeting, -1, file);
        sent.Should().ContainSingle();
        var first = session.Quest.SelectMessageEvents[0];
        var second = session.Quest.SelectMessageEvents[1];
        first.Should().NotBe(second);
        await engine.ExecuteAsync(session, null, second, -1, file);
        session.Money.Should().Be(11);
        session.Quest.QuestMap[62].Should().Be(2);
        session.Quest.QuestMap.Should().NotContainKey(61);
        engine.TryGetEntry(NpcId, 21, QuestProgram.AcceptEvent, 61, out file, out var accept).Should().BeTrue();
        await engine.ExecuteAsync(session, null, accept, -1, file);
        session.Quest.QuestMap[61].Should().Be(1);
    }

    [Fact]
    public async Task SpecificBindingOverridesOnlyTheMatchingQuest()
    {
        File.Delete(Path.Combine(_directory, ScriptName));
        foreach (var (name, binding, quest, coins) in new[]
                 { ("a", "Npc 16079", 61, 1), ("b", "Npc 16079 Zone 21", 61, 2),
                   ("c", "Npc 16079", 62, 3) })
            File.WriteAllText(Path.Combine(_directory, name + ".quest"), $"""
                Bind {binding}
                Quest {quest}
                On topics
                    Topic "Quest {quest}" do
                        Give {coins} coins
                """);
        var (engine, session, _) = CreateHarness();
        session.ZoneId = 21;
        engine.TryGetGreeting(NpcId, 21, out var file, out var greeting).Should().BeTrue();
        await engine.ExecuteAsync(session, null, greeting, -1, file);
        var second = session.Quest.SelectMessageEvents[1];
        await engine.ExecuteAsync(session, null, session.Quest.SelectMessageEvents[0], -1, file);
        session.Money.Should().Be(2);
        await engine.ExecuteAsync(session, null, second, -1, file);
        session.Money.Should().Be(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GreetingRepliesReturnToTheSharedMenuAndSpecialGreetingsKeepTheirOwnChoices(bool special)
    {
        File.Delete(Path.Combine(_directory, ScriptName));
        File.WriteAllText(Path.Combine(_directory, "greeting.quest"), """
            Bind Npc 16079
            On greeting
                If player level >= 10
                    Say "Special"
                    Topic "Special choice" goto close
                Else
                    Say "Shared"
            """);
        File.WriteAllText(Path.Combine(_directory, "quest.quest"), """
            Bind Npc 16079
            Quest 61
            On topics
                Topic "Back" goto greeting
                Topic "Offer" goto details
            On details
                Say "Offer"
                Topic "Back" goto greeting
            """);
        var (engine, session, _) = CreateHarness();
        session.ZoneId = 21;
        session.Level = (byte)(special ? 10 : 1);
        engine.TryGetGreeting(NpcId, 21, out var file, out var greeting).Should().BeTrue();
        await engine.ExecuteAsync(session, null, greeting, -1, file);
        session.Quest.IsScriptDialog.Should().BeTrue();
        if (special)
        {
            session.Quest.SelectMessageEvents.Should().OnlyContain(id => id == -1);
            return;
        }
        session.Quest.SelectMessageEvents[0].Should().Be(greeting);
        await engine.ExecuteAsync(session, null, session.Quest.SelectMessageEvents[1], -1, file);
        session.Quest.SelectMessageEvents[0].Should().Be(greeting);
        await engine.ExecuteAsync(session, null, greeting, -1, file);
        session.Quest.SelectMessageEvents[1].Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Handles_FindsTheQuestScriptForAHelperFileName()
    {
        var (engine, _, _) = CreateHarness();

        engine.Handles("16079_Test.lua").Should().BeTrue();
        engine.Handles("16080_Absent.lua").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_RunsTheDialogAndSendsSelectMessage()
    {
        var (engine, session, sent) = CreateHarness();

        var ran = await engine.ExecuteAsync(session, npc: null, GreetingEvent, -1, "16079_Test.lua");

        ran.Should().BeTrue();
        var packet = sent.Should().ContainSingle().Subject;
        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_SELECT_MSG);
        packet.ReadInt().Should().Be(NpcId);
        packet.ReadByte().Should().Be((byte)Quests.Binding.DialogStyle.Talk);
        packet.ReadInt().Should().Be(61);
        packet.ReadInt().Should().Be(9352);
        packet.ReadInt().Should().Be(27);
    }

    [Fact]
    public async Task ExecuteAsync_DeclinesAnEventTheScriptDoesNotHandle()
    {
        var (engine, session, sent) = CreateHarness();

        var ran = await engine.ExecuteAsync(session, npc: null, UnhandledEvent, -1, "16079_Test.lua");

        ran.Should().BeFalse();
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_StoresTheButtonTargetsServerSideAndNeverOnTheWire()
    {
        var (engine, session, _) = CreateHarness();

        await engine.ExecuteAsync(session, npc: null, GreetingEvent, -1, "16079_Test.lua");

        session.Quest.SelectMessageEvents[0].Should().Be(-1);
    }

    [Fact]
    public async Task NestedScriptsAreAddressedByTheirDeclaredNpcAndKeepTheirOwnButtons()
    {
        var nested = Path.Combine(_directory, "rewards");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "friendly.quest"), """
            Bind Npc 16080
            Quest 777
                Kill 7 of 100
            On greeting
                Say "Hello."
                Topic "Continue" do
                    Give 17 coins
            """);
        var (engine, session, _) = CreateHarness();
        session.Quest.EventNpcId = 16080;

        engine.TryGetGreeting(16080, 1, out var script, out var greeting).Should().BeTrue();
        engine.ObjectivesFor(777)!.Groups[0].Count.Should().Be(7);
        await engine.ExecuteAsync(session, null, greeting, -1, script);
        await engine.ExecuteAsync(session, null, session.Quest.SelectMessageEvents[0], -1,
            session.Quest.ActiveQuestScript);

        session.Money.Should().Be(17);
        engine.Handles("friendly.lua").Should().BeTrue();
    }

    [Fact]
    public void ScriptsWithTheSameBasenameInDifferentFoldersStayDistinct()
    {
        foreach (var (folder, npc) in new[] { ("one", 100), ("two", 200) })
        {
            Directory.CreateDirectory(Path.Combine(_directory, folder));
            File.WriteAllText(Path.Combine(_directory, folder, "npc.quest"),
                $"Bind Npc {npc}\nOn greeting\n    Give {npc} coins\n");
        }
        var (engine, _, _) = CreateHarness();
        engine.TryGetGreeting(100, 1, out var one, out _).Should().BeTrue();
        engine.TryGetGreeting(200, 1, out var two, out _).Should().BeTrue();
        one.Should().NotBe(two);
    }

    [Fact]
    public void HotReloadDetectsEditsToAnOlderFileAndRecoversFromCompileErrors()
    {
        var older = Path.Combine(_directory, "older.quest");
        File.WriteAllText(older, "Bind Npc 100\nOn greeting\n    Give 1 coins\n");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-2));
        var clock = new ManualClock();
        var (engine, _, _) = CreateHarness(clock);
        engine.TryGetGreeting(100, 1, out _, out _).Should().BeTrue();

        File.WriteAllText(older, "Bind Npc 200\nOn greeting\n    This is invalid\n");
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-1));
        clock.Advance();
        engine.TryGetGreeting(100, 1, out _, out _).Should().BeFalse();
        engine.TryGetGreeting(200, 1, out _, out _).Should().BeFalse();

        File.WriteAllText(older, "Bind Npc 200\nOn greeting\n    Give 2 coins\n");
        clock.Advance();
        engine.TryGetGreeting(200, 1, out _, out _).Should().BeTrue();
    }

    [Fact]
    public async Task EditingAnIncludeReloadsItsConsumers()
    {
        var include = Path.Combine(_directory, "words.quest");
        File.WriteAllText(include, "amount = item 100\n");
        File.WriteAllText(Path.Combine(_directory, ScriptName), """
            include words
            Bind Npc 16079
            On greeting
                If player has >= 1 of amount
                    Give 5 coins
            """);
        var clock = new ManualClock();
        var (engine, session, _) = CreateHarness(clock);
        session.Inventory[InventoryConstants.InventoryStart].ItemId = 200;
        session.Inventory[InventoryConstants.InventoryStart].Count = 1;
        engine.TryGetGreeting(NpcId, 1, out var script, out var greeting).Should().BeTrue();
        await engine.ExecuteAsync(session, null, greeting, -1, script);
        session.Money.Should().Be(0);

        File.WriteAllText(include, "amount = item 200\n");
        File.SetLastWriteTimeUtc(include, DateTime.UtcNow.AddSeconds(1));
        clock.Advance();
        var compilation = LibreKO.Quests.QuestCompilation.CreateFromFile(Path.Combine(_directory, ScriptName));
        var condition = (BoundCondition.Predicate)compilation.Program.Events[greeting].Body
            .OfType<BoundStatement.If>().Single().Arms[0].Condition;
        condition.Arguments.GetInt("item").Should().Be(200);
        (await engine.ExecuteAsync(session, null, greeting, -1, script)).Should().BeTrue();
        session.Money.Should().Be(5);
    }

    [Fact]
    public async Task ScriptOnlyKillQuestsResetOnRestartAndSendTheirCounts()
    {
        File.WriteAllText(Path.Combine(_directory, ScriptName), """
            Bind Npc 16079
            Quest 777
                Kill 7 of 100
            On accept for quest 777
                Start 777
            On abandon for quest 777
                Abandon 777
            """);
        var (engine, session, sent) = CreateHarness();
        engine.TryGetEntry(NpcId, 1, QuestProgram.AcceptEvent, 777, out var script, out var accept)
            .Should().BeTrue();
        await engine.ExecuteAsync(session, null, accept, -1, script);
        sent.Should().HaveCount(3);
        sent[0].ReadByte().Should().Be((byte)QuestSubOpcode.Objectives);
        sent[2].ReadByte().Should().Be((byte)QuestSubOpcode.KillCounts);
        session.Quest.GetOrCreateQuestKillCounts(777)[0] = 5;
        engine.TryGetEntry(NpcId, 1, QuestProgram.AbandonEvent, 777, out _, out var abandon);
        await engine.ExecuteAsync(session, null, abandon, -1, script);
        await engine.ExecuteAsync(session, null, accept, -1, script);
        session.Quest.GetQuestKillCounts(777).Should().OnlyContain(count => count == 0);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 4)]
    public async Task JournalAbandonUsesItsScriptAwayFromTheNpcAndPreservesItsGate(byte level, byte expectedState)
    {
        File.WriteAllText(Path.Combine(_directory, ScriptName), """
            Bind Npc 16079
            On abandon for quest 777
                If player level >= 10
                    Abandon 777
            """);
        var (engine, session, _) = CreateHarness();
        session.Level = level;
        session.Quest.QuestMap[777] = 1;
        session.Quest.GetOrCreateQuestKillCounts(777)[0] = 5;
        session.Quest.SelectMessageEvents[0] = 123;
        var runner = new QuestDialogRunner(engine, Substitute.For<ILogger<QuestDialogRunner>>());

        (await runner.TryEntryAsync(session, null, QuestProgram.AbandonEvent, 777)).Should().BeTrue();

        session.Quest.QuestMap[777].Should().Be(expectedState);
        session.Quest.GetQuestKillCounts(777)[0].Should().Be((ushort)(expectedState == 4 ? 0 : 5));
        session.Quest.SelectMessageEvents.Should().OnlyContain(value => value == -1);
        session.Quest.EventNpcId.Should().Be(0);
    }

    [Fact]
    public async Task ScriptButtonsBindRewardSlotsForTheExistingGodotMenu()
    {
        File.WriteAllText(Path.Combine(_directory, ScriptName), """
            Bind Npc 16079
            On greeting
                Say "Choose one."
                Topic "Sword" reward 0 do
                    Exchange 94 for quest
                Topic "Shield" reward 2 goto reward
                Topic "Cancel" goto close
            On reward
                Exchange 94 for quest
            """);
        var (engine, session, sent) = CreateHarness();
        engine.TryGetGreeting(NpcId, 1, out var script, out var greeting).Should().BeTrue();

        await engine.ExecuteAsync(session, null, greeting, -1, script);

        session.Quest.IsScriptDialog.Should().BeTrue();
        session.Quest.SelectMessageRewards.Take(3).Should().Equal(0, 2, -1);
        session.Quest.SelectMessageEvents[0].Should().BeGreaterThanOrEqualTo(QuestProgram.LocalEventBase);
        sent.Should().ContainSingle();
    }

    [Fact]
    public async Task LargeMenusKeepEveryEligibleChoiceAndItsRewardBinding()
    {
        var choices = Enumerable.Range(0, 20).Select(index =>
            $"    If player level >= 0\n        Topic \"Reward {index}\" reward {index % 5} goto reward\n");
        File.WriteAllText(Path.Combine(_directory, ScriptName),
            "Bind Npc 16079\nOn greeting\n    Say \"Choose\"\n" + string.Concat(choices)
            + "    Topic \"Cancel\" goto close\nOn reward\n    Give 1 coins\n");
        var (engine, session, sent) = CreateHarness();
        engine.TryGetGreeting(NpcId, 1, out var script, out var greeting).Should().BeTrue();

        await engine.ExecuteAsync(session, null, greeting, -1, script);

        session.Quest.SelectMessageEvents.Take(20).Should().OnlyContain(id => id >= QuestProgram.LocalEventBase);
        session.Quest.SelectMessageRewards.Take(20).Should().Equal(Enumerable.Range(0, 20).Select(index => index % 5));
        session.Quest.SelectMessageEvents[20].Should().Be(-1);
        var packet = sent.Should().ContainSingle().Subject;
        packet.ReadInt();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadInt();
        for (var index = 0; index < 12; index++) packet.ReadInt();
        packet.ReadSByteString();
        packet.ReadUInt().Should().Be(LibreKO.Common.Gameplay.GameplayProtocol.DialogChoicesMagic);
        packet.ReadByte().Should().Be(1);
        packet.ReadUtf8String().Should().Be("Choose");
        packet.ReadUShort().Should().Be(21);
        for (var index = 0; index < 21; index++)
        {
            packet.ReadInt();
            packet.ReadUtf8String().Should().Be(index < 20 ? $"Reward {index}" : "Cancel");
        }
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public async Task AmbiguousAbandonHandlersCannotFallBackToUnconditionalAbandon()
    {
        var script = "Bind Npc 16079\nOn abandon for quest 777\n    Abandon 777\n";
        File.WriteAllText(Path.Combine(_directory, ScriptName), script);
        var duplicate = Path.Combine(_directory, "duplicate.quest");
        File.WriteAllText(duplicate, script.Replace("16079", "100"));
        var clock = new ManualClock();
        var (engine, session, _) = CreateHarness(clock);
        session.Quest.QuestMap[777] = 1;
        var runner = new QuestDialogRunner(engine, Substitute.For<ILogger<QuestDialogRunner>>());

        (await runner.TryEntryAsync(session, null, QuestProgram.AbandonEvent, 777)).Should().BeTrue();
        session.Quest.QuestMap[777].Should().Be(1);

        File.Delete(duplicate);
        clock.Advance();
        (await runner.TryEntryAsync(session, null, QuestProgram.AbandonEvent, 777)).Should().BeTrue();
        session.Quest.QuestMap[777].Should().Be(4);
    }

    [Fact]
    public async Task ReadinessNotifiesOncePerTransitionAndStaysOffTheNpcClaimPage()
    {
        File.WriteAllText(Path.Combine(_directory, ScriptName), """
            Bind Npc 16079 Zone 21
            Quest 62 "A new hunt"
                Kill 1 of 850
            Rewards
                Give 1 coins
            On offer
                Show quest "Will you help?"
            On ready
                Say "You caught it. Bring it back."
                Topic "On my way" goto close
            On claimable
                Show quest "Here is your reward."
            """);
        var (engine, session, sent) = CreateHarness();
        session.Hp = 100;
        session.ZoneId = 21;
        session.Quest.QuestMap[62] = 1;

        (int Notices, int Pages) Views()
        {
            var notices = 0;
            var pages = 0;
            foreach (var packet in sent)
            {
                var p = ClonePacket(packet);
                if (p.ReadByte() != (byte)QuestSubOpcode.View) continue;
                p.ReadByte(); p.ReadShort(); p.ReadInt(); p.ReadInt();
                var flags = p.ReadByte();
                p.ReadByte();
                if ((flags & 16) != 0) notices++;
                else if (p.ReadByte() == (byte)QuestPageKind.Quest) pages++;
            }
            return (notices, pages);
        }

        await engine.SendViewsAsync(session);
        Views().Notices.Should().Be(0);

        session.Quest.GetOrCreateQuestKillCounts(62)[0] = 1;
        await engine.SendViewsAsync(session, changesOnly: true);
        Views().Notices.Should().Be(1);

        await engine.SendViewsAsync(session, changesOnly: true);
        Views().Notices.Should().Be(1);

        sent.Clear();
        await engine.ShowObjectiveTargetAsync(session, 62, 0);
        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TheStarterRescueCompletesFromItsReadyNotice()
    {
        File.Copy(BakedQuestPath("0_21_500.quest"), Path.Combine(_directory, "0_21_500.quest"), true);
        var (engine, session, sent) = CreateHarness();
        session.Hp = 100;
        session.ZoneId = 21;
        session.Level = 1;
        session.Nation = LibreKO.Common.Enums.AccountNation.ElMorad;
        session.Quest.QuestMap[500] = 1;
        session.Quest.GetOrCreateQuestKillCounts(500)[0] = 1;

        List<(byte Flags, byte State, string[] Topics)> Notices()
        {
            var notices = new List<(byte, byte, string[])>();
            foreach (var packet in sent)
            {
                var p = ClonePacket(packet);
                if (p.ReadByte() != (byte)QuestSubOpcode.View) continue;
                p.ReadByte(); p.ReadShort(); p.ReadInt(); p.ReadInt();
                var flags = p.ReadByte();
                var state = p.ReadByte();
                if ((flags & 16) == 0) continue;
                p.ReadByte(); p.ReadLong();
                p.ReadUtf8String(); p.ReadUtf8String(); p.ReadUtf8String();
                p.ReadByte();
                var groups = p.ReadByte();
                for (var g = 0; g < groups; g++)
                {
                    p.ReadUShort(); p.ReadUShort();
                    var monsters = p.ReadByte();
                    for (var m = 0; m < monsters; m++) p.ReadInt();
                    p.ReadUtf8String(); p.ReadByte();
                }
                for (var list = 0; list < 2; list++)
                {
                    var count = p.ReadUShort();
                    for (var t = 0; t < count; t++) { p.ReadByte(); p.ReadByte(); p.ReadInt(); p.ReadInt(); p.ReadInt(); }
                }
                var topics = new string[p.ReadUShort()];
                for (var t = 0; t < topics.Length; t++) topics[t] = p.ReadUtf8String();
                notices.Add((flags, state, topics));
            }
            return notices;
        }

        await engine.SendViewsAsync(session, 500);
        var ready = Notices().Should().ContainSingle().Subject;
        ready.State.Should().Be((byte)QuestViewState.Claimable);
        ready.Topics.Should().BeEmpty("the rescue completes itself, the notice only tells the story");
        session.Quest.QuestMap[500].Should().Be(2, "Auto complete turns the quest in as soon as the worm is dead");
        session.Quest.NotificationReplies.Should().NotContainKey(500);
    }

    [Fact]
    public void AutoCompleteNeedsAutoAcceptAndNoRewardChoice()
    {
        QuestCompilation.Create("""
            Bind Zone 21
            Quest 900 "Lone hunt"
                Kill 1 of 700
            Auto complete
            On ready
                Say "Done."
            """, "lone.quest").Succeeded.Should().BeFalse("Auto complete without Auto accept is refused");
        QuestCompilation.Create("""
            Bind Zone 21
            Quest 900 "Lone hunt"
                Kill 1 of 700
            Auto accept
            Auto complete
            Rewards
                Choose one
                    Give 1 of 379001000
                    Give 1 of 379002000
            On ready
                Say "Done."
            """, "lone.quest").Succeeded.Should().BeFalse("a reward choice needs an NPC page");
        QuestCompilation.Create("""
            Bind Zone 21
            Quest 900 "Lone hunt"
                Kill 1 of 700
            Auto accept
            Auto complete
            On ready
                Say "Done."
            """, "lone.quest").Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task TheStarterRescueAlsoCompletesFromTheJournalWithNoNpc()
    {
        File.Copy(BakedQuestPath("0_21_500.quest"), Path.Combine(_directory, "0_21_500.quest"), true);
        var (engine, session, _) = CreateHarness();
        session.Hp = 100;
        session.ZoneId = 21;
        session.Level = 1;
        session.Quest.QuestMap[500] = 3;
        session.Quest.GetOrCreateQuestKillCounts(500)[0] = 1;

        engine.IsAutoAccepted(500).Should().BeTrue();
        engine.TryGetEntry(0, 21, QuestProgram.FulfilEvent, 500, out var script, out var fulfil).Should().BeTrue();
        (await engine.ExecuteAsync(session, null, fulfil, -1, script)).Should().BeTrue();
        session.Quest.QuestMap[500].Should().Be(2);
    }

    [Fact]
    public async Task AvailabilityTransitionsNotifyWithoutTalkingAndKeepNpcRepliesSeparate()
    {
        File.WriteAllText(Path.Combine(_directory, ScriptName), """
            Bind Npc 16079 Zone 21
            Quest 62 "A new hunt"
            Requires player level >= 3
            Rewards
                Give 1 coins
            target = location "Patrick" at 830 551 in zone 21
            On available
                Say "Patrick is looking for you."
                Topic "Where?" do
                    Map target
            On offer
                Show quest "Will you help with the hunt?"
            """);
        var (engine, session, sent) = CreateHarness();
        session.Hp = 100;
        session.ZoneId = 21;
        session.Level = 2;
        session.Quest.SelectMessageEvents[0] = 456;
        session.Quest.ActiveQuestScript = "other-npc.quest";
        int Notices() => sent.Count(packet =>
        {
            var p = ClonePacket(packet);
            if (p.ReadByte() != (byte)QuestSubOpcode.View) return false;
            p.ReadByte(); p.ReadShort(); p.ReadInt(); p.ReadInt();
            return (p.ReadByte() & 16) != 0;
        });

        await engine.SendViewsAsync(session);
        Notices().Should().Be(0);
        session.Level = 3;
        await Task.WhenAll(engine.SendViewsAsync(session, changesOnly: true), engine.SendViewsAsync(session, changesOnly: true));
        Notices().Should().Be(1);
        session.Quest.SelectMessageEvents[0].Should().Be(456);
        session.Quest.ActiveQuestScript.Should().Be("other-npc.quest");
        var count = sent.Count;
        await engine.SendViewsAsync(session, changesOnly: true);
        sent.Count.Should().Be(count);
        await engine.ReplyToNotificationAsync(session, 62, 0);
        sent.Count.Should().BeGreaterThan(count);
        count = sent.Count;
        await engine.ReplyToNotificationAsync(session, 62, 0);
        sent.Count.Should().Be(count);
        session.Quest.SelectMessageEvents[0].Should().Be(456);

        session.ZoneId = 22;
        await engine.SendViewsAsync(session, changesOnly: true);
        session.ZoneId = 21;
        await engine.SendViewsAsync(session, changesOnly: true);
        Notices().Should().Be(2);
        session.ZoneId = 22;
        count = sent.Count;
        await engine.ReplyToNotificationAsync(session, 62, 0);
        sent.Count.Should().Be(count);
    }

    [Fact]
    public async Task ManifestLoadsOnlyListedScriptsAndFallbackCanBeDisabled()
    {
        File.WriteAllText(Path.Combine(_directory, "retail.quest"),
            "Bind Npc 16079\nQuest 62 \"Retail hunt\"\nRewards\n    Give 1 coins\n");
        File.WriteAllText(Path.Combine(_directory, "legacy.quest"),
            "Bind Npc 16079\nQuest 999 \"Legacy hunt\"\nRewards\n    Give 2 coins\n");
        File.WriteAllText(Path.Combine(_directory, "quest-manifest.json"), "[\"retail.quest\"]");
        var (engine, session, _) = CreateHarness(manifestOnly: true);
        engine.TextFor(62)!.Title.Should().Be("Retail hunt");
        engine.TextFor(999).Should().BeNull();
        engine.Handles(ScriptName).Should().BeFalse();
        var runner = new QuestDialogRunner(engine, Substitute.For<ILogger<QuestDialogRunner>>());
        (await runner.RunAsync(session, null, GreetingEvent, -1, "legacy.lua")).Should().BeFalse();
        (await runner.RunAsync(session, null, UnhandledEvent, -1, "retail.quest")).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("broken json")]
    [InlineData("[]")]
    [InlineData("[\"../outside.quest\"]")]
    public void ExplicitManifestDoesNotLoadOtherFilesWhenMissingOrInvalid(string? manifest)
    {
        if (manifest is not null)
            File.WriteAllText(Path.Combine(_directory, "quest-manifest.json"), manifest);
        var (engine, _, _) = CreateHarness(manifestOnly: true);
        engine.Handles(ScriptName).Should().BeFalse();
        engine.TryGetGreeting(NpcId, 1, out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task CompileErrorsDoNotRunTheScript()
    {
        File.WriteAllText(Path.Combine(_directory, ScriptName), "Bind Npc broken\n");
        File.WriteAllText(Path.Combine(_directory, "quest-manifest.json"), $"[\"{ScriptName}\"]");
        var (engine, session, _) = CreateHarness(manifestOnly: true);
        var runner = new QuestDialogRunner(engine, Substitute.For<ILogger<QuestDialogRunner>>());
        (await runner.RunAsync(session, null, GreetingEvent, -1, ScriptName)).Should().BeFalse();
    }

    [Fact]
    public void NoScriptInterpreterIsReferencedByTheServerAssembly()
    {
        typeof(QuestScriptEngine).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Should().NotContain(name => name.StartsWith("MoonSharp"));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(3210)]
    public async Task ZoneAutoAcceptanceAndCompletionAreEntirelyScriptDriven(int questId)
    {
        File.WriteAllText(Path.Combine(_directory, ScriptName), $"""
            Bind Zone 21
            Quest {questId} "Rescue"
                Kill 1 of 750
            Requires player level >= 1
            Auto accept
            On started
                Say "Help me!"
            On ready
                Say "You found me."
                Topic "Rescue" do
                    Complete quest
                    Say "Thank you!"
            """);
        var (engine, session, sent) = CreateHarness();
        session.Hp = 100;
        session.Level = 1;
        session.ZoneId = 22;
        engine.IsAutoAccepted(questId).Should().BeTrue();
        await engine.SendViewsAsync(session);
        session.Quest.IsCompleted((short)questId).Should().BeFalse();
        session.Quest.QuestMap.Should().NotContainKey((short)questId);
        session.ZoneId = 21;
        await engine.SendViewsAsync(session);
        session.Quest.StatusOf((short)questId).Should().Be(QuestStatus.Active);
        var count = sent.Count;
        await engine.SendViewsAsync(session, changesOnly: true);
        sent.Count.Should().Be(count);
        var data = Substitute.For<IGameDataService>();
        var progression = new QuestProgressionService(new SessionManager(), data,
            Substitute.For<IQuestDialogRunner>(), engine, Substitute.For<ICharacterStatePersister>(),
            Substitute.For<ILogger<QuestProgressionService>>());
        await progression.CheckQuestKillAsync(session, 850);
        session.Quest.StatusOf((short)questId).Should().Be(QuestStatus.Active);
        await progression.CheckQuestKillAsync(session, 750);
        session.Quest.StatusOf((short)questId).Should().Be(QuestStatus.ReadyToTurnIn);
        await engine.ReplyToNotificationAsync(session, questId, 0);
        session.Quest.IsCompleted((short)questId).Should().BeTrue();
        var (_, restored, _) = CreateHarness();
        restored.LoadQuestData(session.SerializeQuestData());
        restored.Quest.IsCompleted((short)questId).Should().BeTrue();
        restored.ZoneId = 21;
        restored.Hp = 100;
        restored.Level = 1;
        await engine.SendViewsAsync(restored);
        restored.Quest.IsCompleted((short)questId).Should().BeTrue();
        session.Quest.EventNpcId.Should().Be(NpcId);
    }

    private static string BakedQuestPath(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "LibreKO.Game", "Quests", name));

    private (QuestScriptEngine Engine, UserSession Session, List<Packet> Sent) CreateHarness(TimeProvider? clock = null, bool manifestOnly = false)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var sent = new List<Packet>();
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent.Add(ClonePacket(call.Arg<Packet>()));
                return Task.CompletedTask;
            });

        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);
        session.Quest.EventNpcId = NpcId;
        session.ZoneId = 1;

        var gameData = Substitute.For<IGameDataService>();
        gameData.ItemTable.Returns(new Dictionary<int, ItemData>());
        gameData.ItemExchangeTable.Returns(new Dictionary<int, ItemExchangeData>());
        gameData.NpcTable.Returns(new Dictionary<int, NpcData>());
        gameData.MonsterTable.Returns(new Dictionary<int, NpcData>());
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>());

        var effects = new ForwardingEffectApplier();

        var settings = Options.Create(new GameServerSettings { QuestsDirectory = _directory, QuestManifest = manifestOnly ? "quest-manifest.json" : null });
        var engine = new QuestScriptEngine(
            gameData,
            sessionManager,
            effects,
            LibreKO.Quests.Localization.QuestTranslations.Empty,
            TestHostEnvironmentFactory.Create(_directory),
            settings,
            Substitute.For<ILogger<QuestScriptEngine>>(), clock);

        return (engine, session, sent);
    }

    [Fact]
    public async Task AFailedExchangeDoesNotCompleteTheQuest()
    {
        File.WriteAllText(Path.Combine(_directory, ScriptName), """
            Bind Npc 16079
            On fulfil for quest 777
                Exchange 999
                Complete 777
            """);
        var (engine, session, _) = CreateHarness();
        session.Quest.QuestMap[777] = 3;
        engine.TryGetEntry(NpcId, 1, QuestProgram.FulfilEvent, 777, out var script, out var fulfil);
        await engine.ExecuteAsync(session, null, fulfil, -1, script);
        session.Quest.QuestMap[777].Should().Be(3);
    }
    [Fact]
    public void AKillTargetThatNamesANationIsShownAsThatNationsPlayers()
    {
        var (engine, _, _) = CreateHarness();
        engine.MonsterName((int)AccountNation.Karus).Should().Be("Karus players");
        engine.MonsterName((int)AccountNation.ElMorad).Should().Be("El Morad players");
        engine.MonsterName(750).Should().Be("Creature");
    }
}
