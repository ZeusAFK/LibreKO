using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MasteryTests : GameTestBase
{
    private const byte DefenceTree = 6;

    [Theory]
    [InlineData(40)]
    [InlineData(55)]
    [InlineData(83)]
    public async Task AClassTreeFillsUpToTheCharactersLevel(byte level)
    {
        using var provider = CreateProvider(_ => { });
        var (session, client) = CreateMastered(provider, startingLevel: 40);

        await provider.GetRequiredService<IPlayerProgressionService>()
            .ResetToLevelAsync(session, level);

        var pool = session.SkillPoints[ProgressionTable.MasteryPoolSlot];
        var expected = (byte)Math.Min(level, pool);

        await SpendAsync(provider, client, DefenceTree, times: level + 5);

        session.SkillPoints[DefenceTree].Should().Be(expected,
            "a tree accepts points up to the character's level, or until the pool runs dry");
        session.SkillPoints[ProgressionTable.MasteryPoolSlot].Should().Be((byte)(pool - expected));
    }

    [Fact]
    public async Task RaisingTheLevelReopensATreeThatWasFullAtTheOldLevel()
    {
        using var provider = CreateProvider(_ => { });
        var (session, client) = CreateMastered(provider, startingLevel: 40);
        var progression = provider.GetRequiredService<IPlayerProgressionService>();

        await progression.ResetToLevelAsync(session, 40);
        await SpendAsync(provider, client, DefenceTree, times: 60);
        session.SkillPoints[DefenceTree].Should().Be(40, "the tree is capped by the level");

        await progression.ResetToLevelAsync(session, 55);
        await SpendAsync(provider, client, DefenceTree, times: 60);

        session.SkillPoints[DefenceTree].Should().Be(55,
            "the level rose, so the tree must accept more than the old cap");
    }

    [Fact]
    public async Task AMasteryResetReturnsEveryPointToThePool()
    {
        using var provider = CreateProvider(_ => { });
        var (session, client) = CreateMastered(provider, startingLevel: 55);
        var progression = provider.GetRequiredService<IPlayerProgressionService>();

        await progression.ResetToLevelAsync(session, 55);
        var pool = session.SkillPoints[ProgressionTable.MasteryPoolSlot];
        await SpendAsync(provider, client, DefenceTree, times: 30);

        session.ResetMasteryPoints();

        session.SkillPoints[DefenceTree].Should().Be(0);
        session.SkillPoints[ProgressionTable.MasteryPoolSlot].Should().Be(pool);
    }

    private static async Task SpendAsync(ServiceProvider provider, IClient client, byte tree, int times)
    {
        var coordinator = provider.GetRequiredService<ICharacterDevelopmentPacketCoordinator>();
        for (var i = 0; i < times; i++)
        {
            var packet = new Packet(GameOpcodes.GS_SKILLPT_CHANGE);
            packet.WriteByte(tree);
            await coordinator.HandleSkillPointChangeAsync(client, packet);
        }
    }

    private static (UserSession Session, IClient Client) CreateMastered(
        ServiceProvider provider, byte startingLevel)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 910, accountId: 960);
        session.Name = "Mastered";
        session.Class = 206;
        session.Nation = AccountNation.ElMorad;
        session.Level = startingLevel;
        return (session, client);
    }
}
