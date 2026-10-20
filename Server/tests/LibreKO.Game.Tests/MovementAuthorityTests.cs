using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MovementAuthorityTests : GameTestBase
{
    private const byte Moradon = (byte)ZoneId.Moradon;
    private const byte Elsewhere = 1;
    private const float Here = 500f;
    private const short Run = 45;
    private const short Swift = 67;
    private const byte Moving = 3;
    private const int ReportMs = 1500;
    private const float RunStep = 6.75f;
    private const int Steps = 10;
    private const short TownWarp = 21;
    private const int WarpFee = 100;
    private const int Purse = 500;
    private const float TownX = 30;
    private const float TownZ = 40;
    private const short GateObject = 7;
    private const short LeverObject = 8;
    private const short WarpGateType = 5;
    private const short GateType = 1;
    private const short LeverType = 3;
    private const short GateProto = 561;
    private const short LeverProto = 2561;
    private const int Stun = 490500;
    private const byte OperatorSummon = 7;
    private const ushort LootCoins = 200;
    private const float TooFar = 40;
    private const int Freeze = 490501;
    private const short FreezeSeconds = 2;
    private const byte FrozenSpeed = 1;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int milliseconds) => Now = Now.AddMilliseconds(milliseconds);
    }

    [Fact]
    public async Task AnHonestRunIsAcceptedStepByStep()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (runner, sent) = Player(provider, 1001);

        for (var step = 1; step <= Steps; step++)
        {
            clock.Advance(ReportMs);
            await Move(provider, runner, Here + step * RunStep, Here);
        }

        runner.X.Should().BeApproximately(Here + Steps * RunStep, 0.1f);
        Warps(sent).Should().Be(0);
    }

    [Fact]
    public async Task AJumpFartherThanARunIsRefusedAndThePlayerIsPutBack()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (jumper, sent) = Player(provider, 1002);

        clock.Advance(ReportMs);
        await Move(provider, jumper, Here + TooFar, Here);

        jumper.X.Should().Be(Here);
        var back = sent.Single(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WARP);
        back.ResetOffset();
        back.ReadUShort().Should().Be((ushort)(Here * 10));
        back.ReadUShort().Should().Be((ushort)(Here * 10));
    }

    [Fact]
    public async Task ASpeedHackGetsNoFartherThanARunWould()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (hacker, sent) = Player(provider, 1003);
        const float hackStep = 15f;
        const int everyMs = 1000;

        for (var step = 1; step <= Steps; step++)
        {
            clock.Advance(everyMs);
            await Move(provider, hacker, Here + step * hackStep, Here);
        }

        hacker.X.Should().BeLessThan(Here + Steps * hackStep / 2, "twice a run's speed is not kept up");
        Warps(sent).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StandingStillSavesUpOnlyAFewSecondsOfRunning()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (jumper, _) = Player(provider, 1020);
        const int aMinuteMs = 60_000;

        clock.Advance(aMinuteMs);
        await Move(provider, jumper, Here + TooFar, Here);
        jumper.X.Should().Be(Here, "a minute of standing still buys six seconds of running, not a minute's");

        await Move(provider, jumper, Here + 15, Here);
        jumper.X.Should().Be(Here + 15);
    }

    [Fact]
    public async Task AFloodOfTinyStepsGetsNoFartherThanARun()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (flooder, sent) = Player(provider, 1021);
        const int packets = 200;
        const int everyMs = 10;

        for (var step = 1; step <= packets; step++)
        {
            clock.Advance(everyMs);
            await Move(provider, flooder, Here + step, Here);
        }

        flooder.X.Should().BeLessThan(Here + 50, "two seconds of running plus six in hand is all a flood may cover");
        Warps(sent).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AStunThatLandsMidRunLetsTheLastStepInAndHoldsTheNext()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (runner, sent) = Player(provider, 1022);
        const int beforeStun = 4;
        for (var step = 1; step <= beforeStun; step++)
        {
            clock.Advance(ReportMs);
            await Move(provider, runner, Here + step * RunStep, Here);
        }

        const short stunSeconds = 3;
        StunFor(runner, clock, stunSeconds);
        clock.Advance(600);
        var stoppedAt = Here + beforeStun * RunStep + 1.8f;
        await Move(provider, runner, stoppedAt, Here);
        runner.X.Should().Be(stoppedAt, "the client ran until it learnt of the stun");
        Warps(sent).Should().Be(0);

        clock.Advance(1000);
        await Move(provider, runner, stoppedAt + 3f, Here);
        runner.X.Should().Be(stoppedAt, "nothing banked before the stun walks a stunned player");
    }

    [Fact]
    public async Task AStunJustAfterAReportLetsTheRunToItIn()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (runner, sent) = Player(provider, 1030);
        const int beforeStun = 3;
        for (var step = 1; step <= beforeStun; step++)
        {
            clock.Advance(ReportMs);
            await Move(provider, runner, Here + step * RunStep, Here);
        }

        clock.Advance(ReportMs + 10);
        StunFor(runner, clock, 3);
        clock.Advance(40);
        var stoppedAt = Here + beforeStun * RunStep + 6.8f;
        await Move(provider, runner, stoppedAt, Here);

        runner.X.Should().BeApproximately(stoppedAt, 0.1f, "the client reports a frame after 1.5 s, and ran until it learnt of the stun");
        Warps(sent).Should().Be(0);
    }

    [Fact]
    public async Task RefreshingAStunOpensNoNewWayOut()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (held, _) = Player(provider, 1031);
        const int refreshes = 6;
        const int everyMs = 1000;
        const float creep = 2f;
        StunFor(held, clock, 10);

        var x = Here;
        for (var refresh = 0; refresh < refreshes; refresh++)
        {
            clock.Advance(everyMs / 2);
            x += creep;
            await Move(provider, held, x, Here);
            clock.Advance(everyMs / 2);
            held.ReplaceBuff(Stun, Stun, StunBuff(clock, 10));
        }

        held.X.Should().BeLessThan(Here + 5, "a refreshed stun is the same hold, not a new half second of running");
    }

    [Fact]
    public async Task ASlowLimitsTheDistanceSavedUp()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (slowed, _) = Player(provider, 1032);
        slowed.SpeedAmount = 50;
        const int standingReports = 4;
        const int everyMs = 2900;

        for (var report = 0; report < standingReports; report++)
        {
            clock.Advance(everyMs);
            await Move(provider, slowed, Here, Here);
        }
        await Move(provider, slowed, Here + 20, Here);

        slowed.X.Should().Be(Here, "six seconds at half speed are under 17 m, however long the player stood");
    }

    [Fact]
    public async Task ASleepDoesNotHoldThePlayerAsTheClientDoesNot()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (sleeper, _) = Player(provider, 1023);
        sleeper.SetBuff(Stun, new ActiveBuff
        {
            MagicId = Stun, BuffType = BuffType.Sleep, ExpireTicks = clock.Now.AddMinutes(1).UtcTicks,
        });

        clock.Advance(ReportMs);
        await Move(provider, sleeper, Here + 3, Here);

        sleeper.X.Should().Be(Here + 3);
    }

    [Fact]
    public async Task AReportSentBeforeAPullDoesNotUndoIt()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (pulled, sent) = Player(provider, 1024);
        const float pulledTo = Here + 15;

        await provider.GetRequiredService<IWorldMovementService>().DisplaceAsync(pulled, pulledTo, Here);
        await Move(provider, pulled, Here + 1, Here);
        pulled.X.Should().Be(pulledTo);
        Warps(sent).Should().Be(0, "a report already in flight is dropped, not answered");

        clock.Advance(ReportMs);
        await Move(provider, pulled, pulledTo + 3, Here);
        pulled.X.Should().Be(pulledTo + 3);
    }

    [Fact]
    public async Task AReportSentBeforeAGameMasterSummonDoesNotUndoIt()
    {
        using var provider = Provider(new Clock());
        var (summoned, sent) = Player(provider, 1025);
        var (gm, _) = Player(provider, 1026);
        gm.IsGM = true;
        gm.X = Here + 10;
        var packet = new Packet(GameOpcodes.GS_OPERATOR);
        packet.WriteByte(OperatorSummon);
        packet.WriteSByteString(summoned.Name);

        await provider.GetRequiredService<IAdminPacketCoordinator>().HandleOperatorAsync(gm.Client, packet);
        await Move(provider, summoned, Here + 1, Here);

        summoned.X.Should().Be(Here + 10);
        Warps(sent).Should().Be(1, "only the summon's own warp is sent");
    }

    [Fact]
    public async Task AReportSentBeforeAZoneChangeDoesNotUndoIt()
    {
        using var provider = Provider(new Clock());
        var (traveller, sent) = Player(provider, 1028);
        const float arrival = Here + 10;

        await provider.GetRequiredService<IZoneTransitionService>().ChangeZoneAsync(traveller, Moradon, arrival, Here);
        traveller.IsWarping = false;
        await Move(provider, traveller, Here + 1, Here);

        traveller.X.Should().Be(arrival);
        Warps(sent).Should().Be(0, "a report already in flight is dropped, not answered");
    }

    [Fact]
    public async Task AReportSentBeforeARespawnDoesNotUndoIt()
    {
        using var provider = Provider(new Clock());
        var (victim, sent) = Player(provider, 1029);
        const float personArenaX = 710f;
        const float personArenaZ = 466f;
        victim.X = personArenaX;
        victim.Z = personArenaZ;
        victim.ArenaId = ArenaZones.MoradonPersonArena;
        victim.Hp = 0;

        await provider.GetRequiredService<ICombatLifecycleService>().HandleRegeneAsync(victim.Client, victim, 1);
        var respawnedAt = victim.X;
        await Move(provider, victim, personArenaX, personArenaZ);

        victim.X.Should().Be(respawnedAt);
        Warps(sent).Should().Be(0, "a report already in flight is dropped, not answered");
    }

    [Fact]
    public async Task ThePublicDemoSpeedGrantRunsFree()
    {
        using var provider = CreateProvider(_ => { }, _ => { },
            settings => settings.PublicDemo.GrantGameMasterSpeedToEveryone = true);
        var (runner, _) = Player(provider, 1027);

        await Move(provider, runner, Here + 30, Here);

        runner.X.Should().Be(Here + 30, "the demo gives everyone the game master's speed");
    }

    [Fact]
    public async Task ThreeReportsBunchedByAThreeSecondStallAreAccepted()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (runner, sent) = Player(provider, 1006);
        const int beforeStall = 6;
        const int bunched = 3;
        for (var step = 1; step <= beforeStall; step++)
        {
            clock.Advance(ReportMs);
            await Move(provider, runner, Here + step * RunStep, Here);
        }

        clock.Advance(bunched * ReportMs);
        for (var step = beforeStall + 1; step <= beforeStall + bunched; step++)
            await Move(provider, runner, Here + step * RunStep, Here);

        runner.X.Should().BeApproximately(Here + (beforeStall + bunched) * RunStep, 0.1f);
        Warps(sent).Should().Be(0, "a Wi-Fi stall is not a speed hack");
    }

    [Fact]
    public async Task TheStepRightAfterAFreezeEndsRunsAtFullSpeed()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (thawed, sent) = Player(provider, 1007);
        thawed.SetBuff(Freeze, new ActiveBuff
        {
            MagicId = Freeze, BuffType = BuffType.Freeze, Duration = FreezeSeconds, BonusSpeed = FrozenSpeed,
            ExpireTicks = clock.Now.AddSeconds(FreezeSeconds).UtcTicks,
        });
        thawed.SpeedAmount = FrozenSpeed;
        clock.Advance(FreezeSeconds * 1000 + ReportMs);

        await Move(provider, thawed, Here + RunStep, Here);

        thawed.X.Should().BeApproximately(Here + RunStep, 0.1f, "the freeze is over even if the buff sweep has not run yet");
        Warps(sent).Should().Be(0);
    }

    [Fact]
    public async Task ACuredStunIsOverSoTheNextStunAllowsTheRunBeforeIt()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (cured, sent) = Player(provider, 1008);
        const int cureMs = 500;
        const int runMs = 2000;
        const float ranBeforeTheStun = 10f;
        StunFor(cured, clock, 10);
        clock.Advance(cureMs);
        cured.RemoveBuff(Stun);
        await Move(provider, cured, Here + 1, Here);

        clock.Advance(runMs);
        StunFor(cured, clock, 10);
        await Move(provider, cured, Here + 1 + ranBeforeTheStun, Here);

        cured.X.Should().Be(Here + 1 + ranBeforeTheStun, "the second stun is a new hold, not the first one carried on");
        Warps(sent).Should().Be(0);
    }

    [Fact]
    public async Task CuringOneHoldKeepsWhenTheOtherBegan()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (held, _) = Player(provider, 1009);
        const float creep = 5f;
        StunFor(held, clock, 10);
        clock.Advance(1000);
        held.SetBuff(Freeze, new ActiveBuff
        {
            MagicId = Freeze, BuffType = BuffType.Freeze, Duration = 10, BonusSpeed = FrozenSpeed,
            ExpireTicks = clock.Now.AddSeconds(10).UtcTicks,
        });
        clock.Advance(1500);
        await Move(provider, held, Here, Here);
        clock.Advance(500);
        held.ReplaceBuff(Stun, Stun, StunBuff(clock, 10));
        clock.Advance(1000);
        held.RemoveBuff(Freeze);

        await Move(provider, held, Here + creep, Here);

        held.X.Should().Be(Here, "the stun still holds the player from when it first landed");
    }

    [Fact]
    public async Task HonestStepsThatArriveTogetherAfterALagAreAccepted()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (runner, sent) = Player(provider, 1004);
        const int beforeLag = 4;
        for (var step = 1; step <= beforeLag; step++)
        {
            clock.Advance(ReportMs);
            await Move(provider, runner, Here + step * RunStep, Here);
        }

        clock.Advance(2 * ReportMs);
        await Move(provider, runner, Here + (beforeLag + 1) * RunStep, Here);
        await Move(provider, runner, Here + (beforeLag + 2) * RunStep, Here);

        runner.X.Should().BeApproximately(Here + (beforeLag + 2) * RunStep, 0.1f);
        Warps(sent).Should().Be(0);
    }

    [Theory]
    [InlineData(150, true)]
    [InlineData(100, false)]
    public async Task OnlyASpeedBuffKeepsUpASwiftRun(byte speedAmount, bool keptUp)
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (runner, _) = Player(provider, 1005);
        runner.SpeedAmount = speedAmount;
        const float swiftStep = RunStep * 1.5f;
        const int longRun = 30;

        for (var step = 1; step <= longRun; step++)
        {
            clock.Advance(ReportMs);
            await Move(provider, runner, Here + step * swiftStep, Here, Swift);
        }

        (runner.X >= Here + longRun * swiftStep - 0.1f).Should().Be(keptUp);
    }

    [Fact]
    public async Task AStunnedPlayerCannotWalk()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (stunned, _) = Player(provider, 1006);
        const short stunSeconds = 60;
        stunned.SetBuff(Stun, new ActiveBuff
        {
            MagicId = Stun, BuffType = BuffType.Stun, Duration = stunSeconds,
            ExpireTicks = clock.Now.AddSeconds(stunSeconds).UtcTicks,
        });

        clock.Advance(ReportMs);
        await Move(provider, stunned, Here + 5, Here);

        stunned.X.Should().Be(Here, "a player standing when the stun landed may cover only what half a second allows");
    }

    [Fact]
    public async Task AGameMasterMovesFreely()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (gm, _) = Player(provider, 1007);
        gm.IsGM = true;

        await Move(provider, gm, Here + 100, Here);

        gm.X.Should().Be(Here + 100);
    }

    [Fact]
    public async Task AMoveSentBeforeAServerWarpIsDroppedWithoutAnotherWarp()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (blinker, sent) = Player(provider, 1008);

        await provider.GetRequiredService<IWorldMovementService>()
            .WarpAsync(blinker, (ushort)((Here + 40) * 10), (ushort)(Here * 10));
        await Move(provider, blinker, Here + 1, Here);

        blinker.X.Should().Be(Here + 40);
        Warps(sent).Should().Be(1, "only the server's own warp is sent");
    }

    [Theory]
    [InlineData(RunStep, true)]
    [InlineData(30f, false)]
    public async Task OthersSeeALeadOnlyAsFarAsOneStep(float lead, bool kept)
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (runner, _) = Player(provider, 1009);

        clock.Advance(ReportMs);
        await Move(provider, runner, Here + 1, Here, willX: Here + 1 + lead);

        runner.MoveOldWillX.Should().Be((ushort)((kept ? Here + 1 + lead : Here + 1) * 10));
    }

    [Fact]
    public async Task HidingEndsWhenThePlayerReallyMovesWhateverTheLeadSays()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (hider, _) = Player(provider, 1010);
        hider.Invisibility = InvisibilityType.DispelOnMove;
        hider.MoveOldWillX = (ushort)(Here * 10);
        hider.MoveOldWillZ = (ushort)(Here * 10);

        clock.Advance(ReportMs);
        await Move(provider, hider, Here + 2, Here, willX: Here, willZ: Here);

        hider.Invisibility.Should().Be(InvisibilityType.None);
    }

    [Fact]
    public async Task SittingEndsWithAStep()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (sitter, _) = Player(provider, 1011);
        sitter.IsSitting = true;

        clock.Advance(ReportMs);
        await Move(provider, sitter, Here + 2, Here);

        sitter.IsSitting.Should().BeFalse("sitting regenerates faster only while sitting");
    }

    [Theory]
    [InlineData(StateChangeType.Pose, (int)UserPoseState.Sitting, true)]
    [InlineData(StateChangeType.Pose, (int)UserPoseState.Dead, false)]
    [InlineData(StateChangeType.CombatStance, (int)CombatStanceState.Ready, true)]
    [InlineData(StateChangeType.Action, 5, true)]
    [InlineData(StateChangeType.Transformation, 1, false)]
    [InlineData(StateChangeType.Title, 1, false)]
    [InlineData(StateChangeType.PartyLeader, 1, false)]
    public async Task OnlyPosesStancesPartySeekingAndGesturesAreAnnounced(StateChangeType type, int value, bool announced)
    {
        using var provider = Provider(new Clock());
        var (player, sent) = Player(provider, 1012);
        var packet = new Packet(GameOpcodes.GS_STATE_CHANGE);
        packet.WriteByte((byte)type);
        packet.WriteInt(value);

        await provider.GetRequiredService<IWorldMovementService>().HandleStateChangeAsync(player.Client, packet);

        sent.Any(reply => reply.GetOpcode() == (byte)GameOpcodes.GS_STATE_CHANGE).Should().Be(announced);
    }

    [Fact]
    public async Task AZoneChangeTheClientNeverFinishesStopsShieldingThePlayer()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (loader, _) = Player(provider, 1013);
        loader.IsWarping = true;
        loader.WarpingUntilTicks.Should().BeInRange(
            DateTime.UtcNow.AddSeconds(59).Ticks, DateTime.UtcNow.AddSeconds(61).Ticks);

        clock.Advance(ReportMs);
        await Move(provider, loader, Here + 2, Here);
        loader.X.Should().Be(Here, "a player still loading a zone does not move");

        loader.WarpingUntilTicks = DateTime.UtcNow.AddSeconds(-1).Ticks;
        await Move(provider, loader, Here + 2, Here);
        loader.X.Should().Be(Here + 2);
    }

    [Fact]
    public async Task AWarpThatWasNeverOfferedIsRefused()
    {
        using var provider = Provider(new Clock());
        var traveller = WarpTraveller(provider, 1014);

        await PickWarp(provider, traveller);

        traveller.Money.Should().Be(Purse);
        traveller.X.Should().Be(Here);
    }

    [Fact]
    public async Task AnOfferedWarpIsTakenOnceAndOnlyNearWhereItWasOffered()
    {
        using var provider = Provider(new Clock());
        var traveller = WarpTraveller(provider, 1015);
        await OfferWarp(provider, traveller);

        await PickWarp(provider, traveller);
        traveller.Money.Should().Be(Purse - WarpFee);
        traveller.X.Should().Be(TownX);

        traveller.X = Here;
        traveller.Z = Here;
        await PickWarp(provider, traveller);
        traveller.Money.Should().Be(Purse - WarpFee, "one opened list allows one pick");

        await OfferWarp(provider, traveller);
        traveller.X = Here + 30;
        await PickWarp(provider, traveller);
        traveller.Money.Should().Be(Purse - WarpFee, "the player walked away from where the list was opened");
    }

    [Fact]
    public async Task ADeadPlayerCannotPickAWarp()
    {
        using var provider = Provider(new Clock());
        var traveller = WarpTraveller(provider, 1016);
        await OfferWarp(provider, traveller);
        traveller.Hp = 0;

        await PickWarp(provider, traveller);

        traveller.Money.Should().Be(Purse);
    }

    [Fact]
    public async Task AGateEventTogglesOnlyTheGateStandingAtThatObject()
    {
        using var provider = Provider(new Clock());
        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(Moradon, new ObjectEvent
        {
            Index = GateObject, Type = GateType, PosX = Here, PosZ = Here,
        });
        var gate = ObjectNpc(sessionManager, GateProto, GateType, Moradon, Here);
        var otherGate = ObjectNpc(sessionManager, GateProto, GateType, Elsewhere, Here);
        var gateDownTheRoad = ObjectNpc(sessionManager, GateProto, GateType, Moradon, Here + 50);
        var notAGate = ObjectNpc(sessionManager, LeverProto, LeverType, Moradon, Here);
        var (player, _) = Player(provider, 1017);

        await TouchObject(provider, player, GateObject, otherGate.UniqueId);
        otherGate.GateOpen.Should().Be(0, "a gate in another zone is not the gate at this object");
        await TouchObject(provider, player, GateObject, gateDownTheRoad.UniqueId);
        gateDownTheRoad.GateOpen.Should().Be(0, "a gate elsewhere in the zone is not the gate at this object");
        await TouchObject(provider, player, GateObject, notAGate.UniqueId);
        notAGate.GateOpen.Should().Be(0, "a lever is not a gate");

        await TouchObject(provider, player, GateObject, gate.UniqueId);
        gate.GateOpen.Should().Be(1);
    }

    [Fact]
    public async Task ALeverMovesOnlyItsOwnLeverNpc()
    {
        using var provider = Provider(new Clock());
        var sessionManager = provider.GetRequiredService<SessionManager>();
        sessionManager.Maps = CreateMapManagerWithObjectEvent(Moradon, new ObjectEvent
        {
            Index = LeverObject, Type = LeverType, ControlNpcId = GateProto, PosX = Here, PosZ = Here,
        });
        var gate = ObjectNpc(sessionManager, GateProto, GateType, Moradon, Here + 20);
        var lever = ObjectNpc(sessionManager, LeverProto, LeverType, Moradon, Here);
        var stranger = ObjectNpc(sessionManager, LeverProto, LeverType, Elsewhere, Here);
        var (player, _) = Player(provider, 1018);

        await TouchObject(provider, player, LeverObject, stranger.UniqueId);
        stranger.GateOpen.Should().Be(0, "a lever in another zone is not this object's lever");
        gate.GateOpen.Should().Be(1, "the lever still works its own gate");

        await TouchObject(provider, player, LeverObject, lever.UniqueId);
        lever.GateOpen.Should().Be(1);
    }

    [Theory]
    [InlineData(Elsewhere, 0)]
    [InlineData(Moradon, 1)]
    public async Task LootDroppedInAnotherZoneOrRoomCannotBeOpenedOrPickedUp(byte zone, ushort room)
    {
        using var provider = Provider(new Clock());
        var sessionManager = provider.GetRequiredService<SessionManager>();
        var (looter, sent) = Player(provider, 1019);
        looter.Money = Purse;
        var bundle = sessionManager.Regions.CreateBundle(zone, room, looter.X, looter.Z, looter.Y);
        bundle.OwnerCharId = looter.CharacterId;
        bundle.Items.Add(new LootItem { ItemId = InventoryConstants.ItemGold, Count = LootCoins });
        var loot = provider.GetRequiredService<ILootPacketCoordinator>();

        var open = new Packet(GameOpcodes.GS_BUNDLE_OPEN_REQ);
        open.WriteInt(bundle.BundleId);
        await loot.HandleBundleOpenAsync(looter.Client, open);
        var take = new Packet(GameOpcodes.GS_ITEM_GET);
        take.WriteInt(bundle.BundleId);
        take.WriteInt(InventoryConstants.ItemGold);
        take.WriteUShort(0);
        await loot.HandleItemGetAsync(looter.Client, take);

        looter.Money.Should().Be(Purse);
        sessionManager.Regions.GetBundle(bundle.BundleId).Should().NotBeNull();
        sent.Should().NotContain(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_BUNDLE_OPEN_REQ);
    }

    private static ServiceProvider Provider(Clock clock) => CreateProvider(
        _ => { }, _ => { }, configureServices: services => services.AddSingleton<TimeProvider>(clock));

    private static (UserSession Player, List<Packet> Sent) Player(ServiceProvider provider, int id)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var player = sessions.CreateSession(client, id, id + 100);
        player.Name = $"Mover{id}";
        player.ZoneId = Moradon;
        player.Nation = AccountNation.Karus;
        player.Hp = player.MaxHp = 100;
        player.X = Here;
        player.Z = Here;
        sessions.Regions.AddToRegion(player);
        return (player, sent);
    }

    private static Task Move(
        ServiceProvider provider, UserSession mover, float x, float z, short speed = Run,
        float? willX = null, float? willZ = null)
    {
        var packet = new Packet(GameOpcodes.GS_MOVE);
        packet.WriteUShort((ushort)((willX ?? x) * 10));
        packet.WriteUShort((ushort)((willZ ?? z) * 10));
        packet.WriteUShort(0);
        packet.WriteShort(speed);
        packet.WriteByte(Moving);
        packet.WriteUShort((ushort)(x * 10));
        packet.WriteUShort((ushort)(z * 10));
        packet.WriteUShort(0);
        return provider.GetRequiredService<IWorldMovementService>().HandleMoveAsync(mover.Client, packet);
    }

    private static void StunFor(UserSession player, Clock clock, short seconds) =>
        player.SetBuff(Stun, StunBuff(clock, seconds));

    private static ActiveBuff StunBuff(Clock clock, short seconds) => new()
    {
        MagicId = Stun, BuffType = BuffType.Stun, Duration = seconds,
        ExpireTicks = clock.Now.AddSeconds(seconds).UtcTicks,
    };

    private static int Warps(List<Packet> sent) => sent.Count(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_WARP);

    private static UserSession WarpTraveller(ServiceProvider provider, int id)
    {
        provider.GetRequiredService<SessionManager>().Maps = CreateMapManagerWithObjectEvent(
            Moradon,
            new ObjectEvent { Index = 1, Type = WarpGateType, PosX = Here, PosZ = Here },
            new WarpInfo { WarpId = TownWarp, Name = "Town", Fee = WarpFee, Zone = Moradon, X = TownX, Z = TownZ });
        var (player, _) = Player(provider, id);
        player.Money = Purse;
        return player;
    }

    private static Task OfferWarp(ServiceProvider provider, UserSession player) =>
        provider.GetRequiredService<IWorldMovementService>().SendWarpListAsync(
            player, [new WarpListEntry(TownWarp, "Town", string.Empty, Moradon, 0, WarpFee)]);

    private static Task PickWarp(ServiceProvider provider, UserSession player)
    {
        var packet = new Packet(GameOpcodes.GS_WARP_LIST);
        packet.WriteShort(1);
        packet.WriteShort(TownWarp);
        return provider.GetRequiredService<IWorldMovementService>().HandleWarpListAsync(player.Client, packet);
    }

    private static NpcInstance ObjectNpc(SessionManager sessionManager, short proto, short objectType, byte zone, float x) =>
        sessionManager.Regions.SpawnNpc(new NpcInstance
        {
            NpcId = proto, NpcType = NpcData.TypeGate, ZoneId = zone, X = x, Z = Here, SpawnX = x, SpawnZ = Here,
            Hp = 1, MaxHp = 1, Nation = EntityNation.Karus,
            ObjectType = NpcInstance.MapObjectType, ObjectEventType = objectType,
        });

    private static Task TouchObject(ServiceProvider provider, UserSession player, short objectIndex, int npcId)
    {
        var packet = new Packet(GameOpcodes.GS_OBJECT_EVENT);
        packet.WriteShort(objectIndex);
        packet.WriteInt(npcId);
        return provider.GetRequiredService<IWorldObjectEventService>().HandleObjectEventAsync(player.Client, packet);
    }
}
