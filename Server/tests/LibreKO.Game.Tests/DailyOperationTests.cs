using FluentAssertions;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game;
using LibreKO.Game.Scripting;
using LibreKO.Game.World;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class DailyOperationTests
{
    private const int Uid = 1;

    private static (ScriptPlayerQueryService Player, UserSession Session) CreateService()
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var session = new UserSession(client, characterId: 700, accountId: 710) { Name = "Claimer" };
        return (new ScriptPlayerQueryService(
            session, npc: null, Substitute.For<IGameDataService>(), new SessionManager(),
            Substitute.For<ILogger>()), session);
    }

    private static int NowSeconds() => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Fact]
    public void FirstCallIsAllowedAndStampsTheOperation()
    {
        var (lua, session) = CreateService();

        lua.GetUserDailyOp(Uid, (int)DailyOperation.ChaosMap).Should().Be(1);
        session.DailyOps[(int)DailyOperation.ChaosMap].Should().BeCloseTo(NowSeconds(), 5);
    }

    [Fact]
    public void SecondCallWithinTheWindowIsRefused()
    {
        var (lua, _) = CreateService();

        lua.GetUserDailyOp(Uid, (int)DailyOperation.KingWing).Should().Be(1);
        lua.GetUserDailyOp(Uid, (int)DailyOperation.KingWing).Should().Be(0);
        lua.GetUserDailyOp(Uid, (int)DailyOperation.KingWing).Should().Be(0);
    }

    [Fact]
    public void CallIsAllowedAgainOnceTheWindowHasElapsed()
    {
        var (lua, session) = CreateService();
        var op = (int)DailyOperation.KeeperKillerWing;

        lua.GetUserDailyOp(Uid, op).Should().Be(1);
        lua.GetUserDailyOp(Uid, op).Should().Be(0);

        session.DailyOps[op] = NowSeconds()
            - (GameConstants.DailyOperationWindowMinutes + 1) * 60;

        lua.GetUserDailyOp(Uid, op).Should().Be(1);
    }

    [Fact]
    public void ExactlyOnTheWindowBoundaryIsStillRefused()
    {
        var (lua, session) = CreateService();
        var op = (int)DailyOperation.LoyaltyWingReward;

        session.DailyOps[op] = NowSeconds() - GameConstants.DailyOperationWindowMinutes * 60;

        lua.GetUserDailyOp(Uid, op).Should().Be(0);
    }

    [Fact]
    public void OperationsAreIndependentOfEachOther()
    {
        var (lua, _) = CreateService();

        lua.GetUserDailyOp(Uid, (int)DailyOperation.WarderKillerWing1).Should().Be(1);
        lua.GetUserDailyOp(Uid, (int)DailyOperation.WarderKillerWing1).Should().Be(0);

        lua.GetUserDailyOp(Uid, (int)DailyOperation.WarderKillerWing2).Should().Be(1);
        lua.GetUserDailyOp(Uid, (int)DailyOperation.LadderReward).Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(13)]
    [InlineData(255)]
    public void UnknownOperationTypesAreRefused(int opType)
    {
        var (lua, _) = CreateService();

        lua.GetUserDailyOp(Uid, opType).Should().Be(0);
    }

    [Fact]
    public void EveryOperationTypeTheQuestScriptsUseIsSupported()
    {
        var (lua, _) = CreateService();

        foreach (var op in new[] { 1, 4, 5, 7, 9, 10, 11, 12 })
            lua.GetUserDailyOp(Uid, op).Should().Be(1, "quest scripts call op type {0}", op);
    }

    [Fact]
    public void EachOperationRoundTripsThroughItsOwnColumn()
    {
        var mapper = new UserSessionCharacterMapper();
        var (_, session) = CreateService();

        foreach (DailyOperation op in Enum.GetValues<DailyOperation>())
            session.DailyOps[(int)op] = 1_700_000_000 + (int)op;

        var row = new UserDailyOp { CharacterId = session.CharacterId };
        mapper.ApplyToDailyOps(session, row);

        var (_, reloaded) = CreateService();
        mapper.HydrateDailyOps(reloaded, row);

        foreach (DailyOperation op in Enum.GetValues<DailyOperation>())
            reloaded.DailyOps[(int)op].Should().Be(1_700_000_000 + (int)op,
                "operation {0} must use its own column", op);
    }
}
