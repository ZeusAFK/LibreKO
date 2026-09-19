using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using NSubstitute;
using Xunit;

namespace LibreKO.Game.Tests;

public class MagicSecondTypeTests
{
    private const int SkillId = 103007;
    private const int TargetId = 42;

    [Fact]
    public async Task ADamageOverTimeSkillWithABuffRunsBothOfItsTypes()
    {
        var (execution, combat, status, movement) = CreateExecutionService();
        var magic = new MagicData { Id = SkillId, Type1 = 3, Type2 = 4 };

        await execution.ExecuteAsync(null!, magic, SkillId, TargetId, new int[7]);

        await combat.Received(1).ExecuteAsync(
            Arg.Any<UserSession>(), magic, MagicSkillType.OverTime, SkillId, TargetId, Arg.Any<int[]>());
        await status.Received(1).ExecuteAsync(
            Arg.Any<UserSession>(), magic, MagicSkillType.Buff, SkillId, TargetId, Arg.Any<int[]>());
        await movement.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, 0, 0, default!);
    }

    [Fact]
    public async Task ASkillWithOneTypeRunsOnlyThatType()
    {
        var (execution, combat, status, _) = CreateExecutionService();
        var magic = new MagicData { Id = SkillId, Type1 = 1, Type2 = 0 };

        await execution.ExecuteAsync(null!, magic, SkillId, TargetId, new int[7]);

        await combat.Received(1).ExecuteAsync(
            Arg.Any<UserSession>(), magic, MagicSkillType.Melee, SkillId, TargetId, Arg.Any<int[]>());
        await status.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, 0, 0, 0, default!);
    }

    [Fact]
    public async Task EachTypeIsToldWhichTypeItIsRunningAs()
    {
        var (execution, combat, status, _) = CreateExecutionService();
        var magic = new MagicData { Id = SkillId, Type1 = 6, Type2 = 4 };

        await execution.ExecuteAsync(null!, magic, SkillId, TargetId, new int[7]);

        await status.Received(1).ExecuteAsync(
            Arg.Any<UserSession>(), magic, MagicSkillType.Transform, SkillId, TargetId, Arg.Any<int[]>());
        await status.Received(1).ExecuteAsync(
            Arg.Any<UserSession>(), magic, MagicSkillType.Buff, SkillId, TargetId, Arg.Any<int[]>());
        await combat.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!, 0, 0, 0, default!);
    }

    private static (IMagicExecutionService Execution,
                    IMagicCombatEffectService Combat,
                    IMagicStatusEffectService Status,
                    IMagicMovementEffectService Movement) CreateExecutionService()
    {
        var combat = Substitute.For<IMagicCombatEffectService>();
        var status = Substitute.For<IMagicStatusEffectService>();
        var movement = Substitute.For<IMagicMovementEffectService>();

        var gameData = Substitute.For<IGameDataService>();
        gameData.MagicType1Table.Returns(new Dictionary<int, MagicType1Data> { [SkillId] = new() { Id = SkillId } });
        gameData.MagicType3Table.Returns(new Dictionary<int, MagicType3Data> { [SkillId] = new() { Id = SkillId } });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data> { [SkillId] = new() { Id = SkillId } });
        gameData.MagicType6Table.Returns(new Dictionary<int, MagicType6Data> { [SkillId] = new() { Id = SkillId } });

        var execution = new MagicExecutionService(
            new SessionManager(),
            gameData,
            combat,
            status,
            movement,
            Substitute.For<IStealthService>());

        return (execution, combat, status, movement);
    }
}
