using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Protocol.Writers;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestDrakiTests
{
    [Fact]
    public void ARiftChangeReachesTheHostWithBothNumbers()
    {
        var result = QuestCompilation.Create("""
            Bind Npc 25257

            On greeting
                Set draki rift stage 1 substage 4
            """, "draki.quest");
        result.Succeeded.Should().BeTrue(result.RenderDiagnostics());
        var host = Substitute.For<IQuestHost>();
        result.Program.TryGetGreeting(out var entry).Should().BeTrue();

        new QuestInterpreter(result.Program, host).Run(entry).Failure.Should().BeNull();

        host.Received().SetDrakiRift(1, 4);
    }

    [Fact]
    public void TheTimerPacketKeepsTheTwoBytesTheClientSkips()
    {
        var packet = EventPacketWriter.DrakiTimer(2, 5, 300, 17);
        packet.ResetOffset();

        packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_EVENT);
        packet.ReadByte().Should().Be((byte)TempleSubOpcode.DrakiTimer);
        packet.ReadByte().Should().Be(EventPacketWriter.DrakiTimerHeaderFirst);
        packet.ReadByte().Should().Be(EventPacketWriter.DrakiTimerHeaderSecond);
        packet.ReadUShort().Should().Be(2);
        packet.ReadUShort().Should().Be(5);
        packet.ReadInt().Should().Be(300);
        packet.ReadInt().Should().Be(17);
        packet.RemainingBytes.Should().Be(0);
    }

    [Fact]
    public void AStageOutsideTheRiftIsRefused()
    {
        var (context, session) = CreateContext();

        context.Character.SetDrakiRift(0, 6, 1).Should().BeFalse();
        context.Character.SetDrakiRift(0, 1, 9).Should().BeFalse();

        session.DrakiStage.Should().Be(0);
        session.DrakiSubStage.Should().Be(0);
        context.QueuedPackets.Should().BeEmpty();
    }

    [Fact]
    public void TheRiftPositionIsWrittenBackToTheCharacter()
    {
        var (context, session) = CreateContext();
        context.Character.SetDrakiRift(0, 3, 4).Should().BeTrue();

        var character = new Character();
        new UserSessionCharacterMapper().ApplyToCharacter(session, character);

        character.DrakiStage.Should().Be(3);
        character.DrakiSubStage.Should().Be(4);
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
