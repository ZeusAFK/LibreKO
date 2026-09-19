using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class RedistributionPanelTests
{
    [Fact]
    public void OpenStatsPanelOpensTheClassChangeWindowLikeTheJobChangeNpc()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);
        var context = new QuestScriptContext(session, npc: null, Substitute.For<IGameDataService>(),
            sessionManager, Substitute.For<ILogger>(), expMultiplier: 1);

        context.Dialog.SendStatSkillDistribute();
        context.Dialog.SendJobChangePanel();

        context.QueuedPackets.Should().HaveCount(2);
        foreach (var packet in context.QueuedPackets)
        {
            packet.GetOpcode().Should().Be((byte)GameOpcodes.GS_CLASS_CHANGE);
            packet.GetBytes()[1].Should().Be((byte)ClassChangeSubOpcode.Eligibility);
        }
    }
}
