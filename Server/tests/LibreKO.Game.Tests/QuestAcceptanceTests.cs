using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Configuration;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestAcceptanceTests
{
    private const int HelperIndex = 4242;
    private const short QuestId = 777;

    [Theory]
    [InlineData(QuestSubOpcode.AcceptUnlessCompleted)]
    [InlineData(QuestSubOpcode.AcceptUnlessReadyToTurnIn)]
    [InlineData(QuestSubOpcode.NpcEvent)]
    public async Task HelperIndexRequestsCannotChangeAnyQuestState(QuestSubOpcode sub)
    {
        var (service, session, client, sent) = CreateHarness();
        await service.HandleQuestAsync(client, Request(sub));
        session.Quest.QuestMap.Should().BeEmpty();
        for (byte state = 0; state <= 4; state++)
        {
            session.Quest.QuestMap[QuestId] = state;
            await service.HandleQuestAsync(client, Request(sub));
            session.Quest.QuestMap[QuestId].Should().Be(state);
        }
        session.Quest.ActiveQuestId.Should().Be(0);
        sent.Should().BeEmpty();
    }

    private static Packet Request(QuestSubOpcode sub)
    {
        var packet = new Packet(GameOpcodes.GS_QUEST);
        packet.WriteByte((byte)sub);
        packet.WriteInt(HelperIndex);
        packet.ResetOffset();
        return packet;
    }

    private static (IQuestProgressionService Service, UserSession Session, IClient Client, List<Packet> Sent)
        CreateHarness()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());

        var sent = new List<Packet>();
        client.SendPacket(Arg.Any<Packet>()).Returns(callInfo =>
        {
            var source = callInfo.Arg<Packet>();
            var clone = new Packet(source.GetOpcode());
            clone.WriteBytes(source.GetData());
            clone.ResetOffset();
            sent.Add(clone);
            return Task.CompletedTask;
        });

        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);
        session.Hp = 100;

        var gameData = Substitute.For<IGameDataService>();

        var service = new QuestProgressionService(
            sessionManager, gameData, Substitute.For<IQuestDialogRunner>(),
            Substitute.For<IQuestDefinitionSource>(),
            Substitute.For<ICharacterStatePersister>(),
            Substitute.For<ILogger<QuestProgressionService>>());

        return (service, session, client, sent);
    }
}
