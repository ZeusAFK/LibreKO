using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class QuestExperienceRewardTests : GameTestBase
{
    private const long MaxExpPerLevel = 100;

    private sealed record Harness(
        ServiceProvider Provider, IScriptEffectApplier Applier,
        UserSession Session, IGameDataService GameData) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private static Harness Build()
    {
        var provider = CreateProvider(
            _ => { },
            gameData =>
            {
                gameData.GetMaxExpForLevel(Arg.Any<byte>()).Returns(MaxExpPerLevel);
                gameData.GetCoefficient(Arg.Any<short>()).Returns(CreateBasicCoefficient(101));
            });

        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        client.SendPacket(Arg.Any<Packet>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessionManager = provider.GetRequiredService<SessionManager>();
        var session = sessionManager.CreateSession(client, characterId: 1, accountId: 1);
        session.Class = 101;
        session.Level = 10;
        sessionManager.Regions.AddToRegion(session);

        return new Harness(
            provider,
            provider.GetRequiredService<IScriptEffectApplier>(),
            session,
            provider.GetRequiredService<IGameDataService>());
    }

    private static async Task AwardAsync(Harness harness, params int[] amounts)
    {
        var context = new QuestScriptContext(
            harness.Session, npc: null, harness.GameData,
            harness.Provider.GetRequiredService<SessionManager>(),
            Substitute.For<ILogger>(), expMultiplier: 1);
        foreach (var amount in amounts)
            context.Character.ExpChange(harness.Session.CharacterId, amount);
        await harness.Applier.ApplyAsync(harness.Session, context, "reward.quest");
    }

    [Fact]
    public async Task ThreeQuestsInARowEachPayTheirExperience()
    {
        using var harness = Build();
        harness.Session.Experience = 99;

        await AwardAsync(harness, 20);

        harness.Session.Level.Should().Be(11, "99 + 20 crosses the level maximum");
        harness.Session.Experience.Should().Be(19, "the 19 above the maximum carries over");

        await AwardAsync(harness, 20);
        harness.Session.Experience.Should().Be(39, "a reward after a level-up still lands");

        await AwardAsync(harness, 20);
        harness.Session.Level.Should().Be(11);
        harness.Session.Experience.Should().Be(59, "and so does the next one");
    }

    [Fact]
    public async Task AnExperienceRewardAtTheLevelMaximumIsNotDropped()
    {
        using var harness = Build();
        harness.Session.Experience = MaxExpPerLevel;

        await AwardAsync(harness, 20);

        harness.Session.Level.Should().Be(11);
        harness.Session.Experience.Should().Be(20);
    }

    [Fact]
    public async Task OneRewardCanCrossSeveralLevels()
    {
        using var harness = Build();

        await AwardAsync(harness, 350);

        harness.Session.Level.Should().Be(13);
        harness.Session.Experience.Should().Be(50);
    }

    [Fact]
    public async Task TwoAwardsInOneScriptRunAreSummed()
    {
        using var harness = Build();

        await AwardAsync(harness, 60, 60);

        harness.Session.Level.Should().Be(11);
        harness.Session.Experience.Should().Be(20);
    }
}
