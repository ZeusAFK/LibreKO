using System.Text;
using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Gameplay;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestDialogTextTests : GameTestBase, IDisposable
{
    [Fact]
    public void DialogAndSpeechWritersMatchTheGodotDecoderFixtures()
    {
        var dialog = LibreKO.Game.Protocol.Writers.NpcDialogPacketWriter.SelectMessage(
            100, 2, 777, -1, [-1, 10], 12, "a.quest", "Choose", ["Sword", ""]);
        Convert.ToHexString(dialog.GetData()).Should().Be("640000000209030000FFFFFFFFFFFFFFFF0A000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF07612E7175657374444C473101060043686F6F736502050053776F72640000");
        var speech = LibreKO.Game.Protocol.Writers.NpcDialogPacketWriter.NpcSay([], ["Hola", "Bye"]);
        Convert.ToHexString(speech.GetData()).Should().Be("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF444C473101020400486F6C610300427965");
    }

    [Fact]
    public void LargeDialogWriterMatchesTheGodotFixture()
    {
        var labels = Enumerable.Range(0, 13).Select(index => ((char)('A' + index)).ToString()).ToArray();
        var packet = LibreKO.Game.Protocol.Writers.NpcDialogPacketWriter.SelectMessage(
            100, 2, 777, -1, Enumerable.Repeat(-1, 13).ToArray(), 12, "a.quest", "Choose", labels);
        Convert.ToHexString(packet.GetData()).Should().Be("640000000209030000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF07612E7175657374444C473201060043686F6F73650D00FFFFFFFF010041FFFFFFFF010042FFFFFFFF010043FFFFFFFF010044FFFFFFFF010045FFFFFFFF010046FFFFFFFF010047FFFFFFFF010048FFFFFFFF010049FFFFFFFF01004AFFFFFFFF01004BFFFFFFFF01004CFFFFFFFF01004D");
    }

    private const int NpcId = 16079;
    private const int GreetingEvent = 1205;
    private const string English = "Bring her ten Vouchers of Chaos.";
    private const string Spanish = "Tráele diez Vales del Caos.";
    private const string CloseEnglish = "Close";
    private const string CloseSpanish = "Cerrar";

    private static readonly string Script = $"""
        Bind Npc 16079

        greeting_topic = event {GreetingEvent}

        On greeting_topic for quest 61
            Say "{English}"
            Topic "{CloseEnglish}" goto close
        """;

    private static readonly string Po = $"""
        msgid ""
        msgstr ""
        "Language: es\n"

        msgid "{English}"
        msgstr "{Spanish}"

        msgid "{CloseEnglish}"
        msgstr "{CloseSpanish}"
        """;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "kqt-" + Guid.NewGuid().ToString("N"));

    public QuestDialogTextTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "16079_Test.quest"), Script, new UTF8Encoding(false));
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
    public async Task TheDialogCarriesTheEnglishWordsAfterTheRetailPayload()
    {
        var (engine, session, sent) = CreateHarness();

        await engine.ExecuteAsync(session, npc: null, GreetingEvent, -1, "16079_Test.lua");

        var (header, labels) = ReadTrailer(sent.Should().ContainSingle().Subject);
        header.Should().Be(English);
        labels.Should().ContainSingle().Which.Should().Be(CloseEnglish);
    }

    [Fact]
    public async Task ASpanishAccountGetsTheTranslationFromThePoFile()
    {
        var (engine, session, sent) = CreateHarness();
        session.Language = GameLanguage.Spanish;

        await engine.ExecuteAsync(session, npc: null, GreetingEvent, -1, "16079_Test.lua");

        var (header, labels) = ReadTrailer(sent.Should().ContainSingle().Subject);
        header.Should().Be(Spanish);
        labels.Should().ContainSingle().Which.Should().Be(CloseSpanish);
    }

    [Fact]
    public async Task ALanguageWithNoPoFileFallsBackToTheWordsInTheScript()
    {
        var (engine, session, sent) = CreateHarness(loadSpanish: false);
        session.Language = GameLanguage.Spanish;

        await engine.ExecuteAsync(session, npc: null, GreetingEvent, -1, "16079_Test.lua");

        var (header, _) = ReadTrailer(sent.Should().ContainSingle().Subject);
        header.Should().Be(English);
    }

    [Fact]
    public async Task TheRetailPayloadStaysIntactSoAnOlderClientStillReadsTheDialog()
    {
        var (engine, session, sent) = CreateHarness();

        await engine.ExecuteAsync(session, npc: null, GreetingEvent, -1, "16079_Test.lua");

        var packet = sent.Should().ContainSingle().Subject;
        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_SELECT_MSG);
        packet.ReadInt().Should().Be(NpcId);
        packet.ReadByte().Should().Be((byte)Quests.Binding.DialogStyle.Talk);
        packet.ReadInt().Should().Be(61);
        packet.ReadInt().Should().Be(-1);
    }

    private static (string Header, List<string> Labels) ReadTrailer(Packet packet)
    {
        packet.ReadInt();
        packet.ReadByte();
        packet.ReadInt();
        packet.ReadInt();
        for (var i = 0; i < UserSession.SelectMessageEventCount; i++)
            packet.ReadInt();
        packet.ReadSByteString();

        packet.ReadUInt().Should().Be(GameplayProtocol.DialogTextMagic);
        packet.ReadByte().Should().Be(GameplayProtocol.ExtensionVersion);

        var header = packet.ReadUtf8String();
        var count = packet.ReadByte();
        var labels = new List<string>(count);
        for (var i = 0; i < count; i++)
            labels.Add(packet.ReadUtf8String());
        return (header, labels);
    }

    private (QuestScriptEngine Engine, UserSession Session, List<Packet> Sent) CreateHarness(
        bool loadSpanish = true)
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

        var gameData = Substitute.For<IGameDataService>();
        gameData.ItemTable.Returns(new Dictionary<int, ItemData>());
        gameData.ItemExchangeTable.Returns(new Dictionary<int, ItemExchangeData>());
        gameData.NpcTable.Returns(new Dictionary<int, NpcData>());
        gameData.MonsterTable.Returns(new Dictionary<int, NpcData>());
        gameData.ZoneInfoTable.Returns(new Dictionary<short, ZoneInfoData>());

        var effects = new ForwardingEffectApplier();

        var translations = new QuestTranslations();
        if (loadSpanish)
            translations.Load("es", Po.Split('\n'));

        var engine = new QuestScriptEngine(
            gameData,
            sessionManager,
            effects,
            translations,
            TestHostEnvironmentFactory.Create(_directory),
            Options.Create(new GameServerSettings { QuestsDirectory = _directory }),
            Substitute.For<ILogger<QuestScriptEngine>>());

        return (engine, session, sent);
    }
}
