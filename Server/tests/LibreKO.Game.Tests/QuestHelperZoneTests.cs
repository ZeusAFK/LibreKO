using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestHelperZoneTests : GameTestBase, IDisposable
{
    private const short OsirisProtoId = 31526;
    private const byte MoradonZone = 21;
    private const byte ElMoradZone = 2;

    private const string OsirisScript = """
        Bind Npc 31526

        On greeting
            Say "Greetings! I'm Osiris, a [White Shadow Commander]."
            Topic "Close" goto close
        """;

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "kq-" + Guid.NewGuid().ToString("N"));

    public QuestHelperZoneTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, $"{OsirisProtoId}.quest"), OsirisScript);
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

    private async Task<UserSession> TalkToOsirisIn(byte zoneId)
    {
        using var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetNpc(OsirisProtoId, Arg.Any<bool>()).Returns(new NpcData
                {
                    Id = OsirisProtoId,
                    Name = "Osiris",
                    NpcType = 213,
                    IsMonster = false,
                });
            },
            settings => settings.QuestsDirectory = _directory);

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 800, accountId: 810);
        session.Name = "Talker";
        session.ZoneId = zoneId;
        session.Hp = 100;
        session.Class = 101;
        session.Level = 60;
        session.X = 1665;
        session.Z = 396;
        sessionManager.Regions.AddToRegion(session);

        var osiris = sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = OsirisProtoId,
            Name = "Osiris",
            NpcType = 213,
            ZoneId = zoneId,
            X = 1665,
            Z = 396,
            SpawnX = 1665,
            SpawnZ = 396,
            Hp = 100,
            MaxHp = 100,
        });

        var packet = new Packet(GameOpcodes.GS_NPC_EVENT);
        packet.WriteByte(1);
        packet.WriteInt(osiris.UniqueId);
        packet.ResetOffset();

        await provider.GetRequiredService<IQuestNpcInteractionService>()
            .HandleNpcEventAsync(client, packet);

        return session;
    }

    [Fact]
    public async Task AScriptWithoutAZoneGreetsInEveryZone()
    {
        var session = await TalkToOsirisIn(ElMoradZone);

        session.Quest.ActiveQuestScript.Should().Be($"@npc_{OsirisProtoId}_{ElMoradZone}.quest",
            "Osiris must answer outside the zone his helper rows used to name");
    }

    [Fact]
    public async Task TheScriptsOwnZoneStillGreets()
    {
        var session = await TalkToOsirisIn(MoradonZone);

        session.Quest.ActiveQuestScript.Should().Be($"@npc_{OsirisProtoId}_{MoradonZone}.quest");
    }
}
