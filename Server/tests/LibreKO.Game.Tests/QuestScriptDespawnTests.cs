using FluentAssertions;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using LibreKO.Quests;
using LibreKO.Quests.Runtime;
using LibreKO.Quests.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class QuestScriptDespawnTests
{
    private const string Script = """
        Bind Npc 25178

        On greeting
            Cast 504005
            Despawn npc
        """;

    private sealed record Fixture(
        UserSession Session, NpcInstance? Npc, QuestScriptContext Context, SessionManager Sessions);

    private static Fixture World(bool withNpc = true)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sessions = new SessionManager();
        var session = sessions.CreateSession(client, 1, 1);
        var npc = withNpc
            ? new NpcInstance
            {
                IsMonster = true,
                UniqueId = 4242, NpcId = 25178, Name = "Spirit Of Logos",
                MaxHp = 70000, Hp = 70000, ZoneId = session.ZoneId,
                RespawnType = NpcRespawnType.Normal, RespawnDelayMs = 32_400_000,
            }
            : null;
        var context = new QuestScriptContext(
            session, npc, Substitute.For<IGameDataService>(), sessions,
            Substitute.For<ILogger>(), 1);
        return new Fixture(session, npc, context, sessions);
    }

    private static Task ApplyAsync(Fixture fixture)
    {
        var provider = new ServiceCollection()
            .AddSingleton<INpcLifecycleService>(new NpcLifecycleService(
                fixture.Sessions, Substitute.For<ILogger<NpcLifecycleService>>()))
            .BuildServiceProvider();
        var applier = new ScriptEffectApplier(
            Substitute.For<IGameDataService>(),
            Substitute.For<ICharacterStatePersister>(),
            provider,
            Substitute.For<ILogger<ScriptEffectApplier>>());
        return applier.ApplyAsync(fixture.Session, fixture.Context, "despawn.quest");
    }

    [Fact]
    public void TheScriptCompilesAndAsksTheHostToDespawn()
    {
        var compilation = QuestCompilation.Create(Script, "despawn.quest");
        compilation.Diagnostics.Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);

        var host = Substitute.For<IQuestHost>();
        new QuestInterpreter(compilation.Program, host)
            .Run(compilation.Program.EventNames["greeting"]);

        host.Received(1).DespawnNpc();
    }

    [Fact]
    public async Task ADespawnedNpcIsDeadAndWaitsItsOwnRespawnDelay()
    {
        var fixture = World();
        fixture.Context.RequestNpcDespawn();

        await ApplyAsync(fixture);

        fixture.Npc!.IsDead.Should().BeTrue();
        fixture.Npc.Hp.Should().Be(0);
        fixture.Npc.State.Should().Be(NpcState.Dead);
        fixture.Npc.RespawnDelayMs.Should().Be(32_400_000, "the spawn row asks for nine hours");
        fixture.Npc.CanRespawn.Should().BeTrue("a despawn is not a permanent deletion");
    }

    [Fact]
    public async Task ADespawnLeavesTheLastKilledNpcAlone()
    {
        var fixture = World();
        fixture.Session.LastKilledNpcId = 1234;
        fixture.Context.RequestNpcDespawn();

        await ApplyAsync(fixture);

        fixture.Session.LastKilledNpcId.Should().Be(1234,
            "removing a talked-to NPC is not a kill and must not feed NpcKillID");
    }

    [Fact]
    public async Task AFailedActionCancelsTheDespawn()
    {
        var fixture = World();
        fixture.Context.RequestNpcDespawn();
        fixture.Context.FailAction("no room");

        await ApplyAsync(fixture);

        fixture.Npc!.IsDead.Should().BeFalse();
    }

    [Fact]
    public async Task AScriptWithNoNpcDespawnsNothing()
    {
        var fixture = World(withNpc: false);
        fixture.Context.RequestNpcDespawn();

        var apply = async () => await ApplyAsync(fixture);

        await apply.Should().NotThrowAsync();
    }

    [Fact]
    public void TheScriptContextAsksForTheSameDespawn()
    {
        var fixture = World();

        fixture.Context.Npc.SendNpcKillID(0, 25178).Should().BeTrue();

        fixture.Context.DespawnEventNpc.Should().BeTrue(
            "both engines must answer SendNpcKillID the same way");
    }
}
