using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class AttackTimingAndReachTests : GameTestBase
{
    private const int Sword = 110150001;
    private const short SwordDelay = 150;
    private const int Haste = 490900;
    private const short HasteAttackSpeed = 150;
    private const byte Zone = 21;
    private const float Here = 500f;
    private const byte Starting = 1;
    private const byte Moving = 3;
    private const float SprintStep = 6.75f;
    private const float RunStep = 6.75f;
    private const short Sprint = 67;

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int milliseconds) => Now = Now.AddMilliseconds(milliseconds);
    }

    [Fact]
    public async Task ASwingBeforeTheWeaponIsReadyIsRefused()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (attacker, sent) = Attacker(provider);
        var worm = Monster(provider, Here + 1);

        await Swing(provider, attacker, worm);
        clock.Advance(1000);
        await Swing(provider, attacker, worm);
        Swings(sent).Should().Be(1, "a 1.5 s weapon cannot swing again after 1 s");

        clock.Advance(200);
        await Swing(provider, attacker, worm);
        Swings(sent).Should().Be(2);
    }

    [Fact]
    public async Task AnAttackSpeedBuffShortensTheSwing()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (attacker, sent) = Attacker(provider);
        attacker.SetBuff(Haste, new ActiveBuff
        {
            MagicId = Haste, CasterId = attacker.CharacterId, BuffType = BuffType.AttackSpeed,
            ExpireTicks = DateTime.UtcNow.AddMinutes(1).Ticks,
        });
        var worm = Monster(provider, Here + 1);

        await Swing(provider, attacker, worm);
        clock.Advance(700);
        await Swing(provider, attacker, worm);

        Swings(sent).Should().Be(2);
    }

    [Fact]
    public async Task TwoReportsAMomentApartGiveNoReach()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (attacker, sent) = Attacker(provider);
        const float nudge = 0.1f;
        var worm = Monster(provider, Here + nudge + 14.1f);

        await Move(provider, attacker, Sprint, Moving);
        clock.Advance(10);
        await Move(provider, attacker, Sprint, Moving, Here + nudge);
        clock.Advance(1000);
        await Swing(provider, attacker, worm);

        Swings(sent).Should().Be(0, "a tenth of a metre covered is a tenth of a metre of reach");
    }

    [Fact]
    public async Task ASwingStandsThePlayerUp()
    {
        using var provider = Provider(new Clock());
        var (attacker, sent) = Attacker(provider);
        attacker.IsSitting = true;
        var worm = Monster(provider, Here + 1);

        await Swing(provider, attacker, worm);

        Swings(sent).Should().Be(1);
        attacker.IsSitting.Should().BeFalse("sitting regenerates faster only while sitting");
    }

    [Fact]
    public async Task ATargetFarAwayIsNotAttacked()
    {
        using var provider = Provider(new Clock());
        var (attacker, sent) = Attacker(provider);
        var worm = Monster(provider, Here + 50);

        await Swing(provider, attacker, worm);

        Swings(sent).Should().Be(0);
        worm.Hp.Should().Be(worm.MaxHp);
    }

    [Theory]
    [InlineData(0, Moving, 1000, SprintStep, false)]
    [InlineData(Sprint, Moving, 1000, SprintStep, true)]
    [InlineData(Sprint, Starting, 1000, SprintStep, false)]
    [InlineData(Sprint, Moving, 1000, 0f, false)]
    [InlineData(Sprint, WorldMovementService.MoveEchoFinish, 1000, SprintStep, false)]
    [InlineData(Sprint, Moving, 5000, SprintStep, false)]
    public async Task ARunningAttackerReachesWhatItsLastReportLagsBehind(
        short speed, byte echo, int sinceReportMs, float lastStepSpeed, bool reached)
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (attacker, sent) = Attacker(provider);
        attacker.MoveOldSpeed = speed;
        attacker.MoveOldEcho = echo;
        attacker.LastStepSpeed = lastStepSpeed;
        attacker.LastStepDistance = lastStepSpeed * MovePace.ReportSeconds;
        attacker.LastMoveTicks = clock.Now.AddMilliseconds(-sinceReportMs).UtcTicks;
        var worm = Monster(provider, Here + 14);

        await Swing(provider, attacker, worm);

        (Swings(sent) == 1).Should().Be(reached,
            "a sprinter may be a second's run past its last report, but not after it stopped, went quiet or stood still");
    }

    [Theory]
    [InlineData(Moving, true)]
    [InlineData(WorldMovementService.MoveEchoFinish, false)]
    public async Task ARunningTargetIsReachedWhereItsLastReportLagsBehind(byte echo, bool reached)
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (attacker, sent) = Attacker(provider);
        var sessions = provider.GetRequiredService<SessionManager>();
        var runnerClient = Substitute.For<IClient>();
        runnerClient.Id.Returns(Guid.NewGuid());
        var runner = sessions.CreateSession(runnerClient, characterId: 912, accountId: 913);
        runner.ZoneId = Zone;
        runner.X = Here + 14;
        runner.Z = Here;
        runner.Hp = runner.MaxHp = 100;
        runner.MoveOldSpeed = Sprint;
        runner.MoveOldEcho = echo;
        runner.LastStepSpeed = SprintStep;
        runner.LastStepDistance = SprintStep;
        runner.LastMoveTicks = clock.Now.AddSeconds(-1).UtcTicks;
        sessions.Regions.AddToRegion(runner);

        await SwingAt(provider, attacker, runner.CharacterId);

        (Swings(sent) == 1).Should().Be(reached);
    }

    [Fact]
    public async Task AMoveReportStartsTheRunningAllowanceAndAStopEndsIt()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (attacker, sent) = Attacker(provider);
        var nearWorm = Monster(provider, Here + 14);
        var worm = Monster(provider, Here + RunStep + 14);

        await Move(provider, attacker, Sprint, Starting);
        clock.Advance(1000);
        await Swing(provider, attacker, nearWorm);
        Swings(sent).Should().Be(0, "a report that the run starts is not yet a run");

        clock.Advance(500);
        await Move(provider, attacker, Sprint, Moving, Here + RunStep);
        clock.Advance(1000);
        await Swing(provider, attacker, worm);
        Swings(sent).Should().Be(1);

        await Move(provider, attacker, Sprint, Moving);
        clock.Advance(1400);
        await Swing(provider, attacker, worm);
        Swings(sent).Should().Be(1, "a report of running that went nowhere gives no allowance");

        await Move(provider, attacker, 0, WorldMovementService.MoveEchoFinish);
        clock.Advance(2000);
        await Swing(provider, attacker, worm);
        Swings(sent).Should().Be(1, "a stopped player is where it said");
    }

    [Fact]
    public async Task BareHandsSwingWithTheSameMarginAsAWeapon()
    {
        var clock = new Clock();
        using var provider = Provider(clock);
        var (attacker, sent) = Attacker(provider);
        attacker.Inventory[InventoryConstants.RightHand].ItemId = 0;
        attacker.Inventory[InventoryConstants.RightHand].Count = 0;
        var worm = Monster(provider, Here + 1);
        const int swings = 4;
        const int everyMs = 900;

        for (var swing = 0; swing < swings; swing++)
        {
            await Swing(provider, attacker, worm);
            clock.Advance(everyMs);
        }

        Swings(sent).Should().Be(swings, "a fist swings every second, and a client a tenth fast still lands");
    }

    [Fact]
    public async Task ATargetInAnotherRoomIsNotAttacked()
    {
        using var provider = Provider(new Clock());
        var (attacker, sent) = Attacker(provider);
        var worm = Monster(provider, Here + 1, room: 1);

        await Swing(provider, attacker, worm);

        Swings(sent).Should().Be(0);
    }

    [Fact]
    public async Task ALargeMonsterIsReachedAtItsBodysEdge()
    {
        using var provider = Provider(new Clock());
        var (attacker, sent) = Attacker(provider);
        var dragon = Monster(provider, Here + 18, bulk: 1000);

        await Swing(provider, attacker, dragon);

        Swings(sent).Should().Be(1, "a 10 m wide body is struck from far off its centre");
    }

    private static ServiceProvider Provider(Clock clock) => CreateProvider(_ => { }, gameData =>
    {
        gameData.GetItem(Sword).Returns(new ItemData { Num = Sword, Kind = 21, Slot = 1, Delay = SwordDelay, Range = 10, Damage = 10, Duration = 100 });
        gameData.GetMagic(Haste).Returns(new MagicData { Id = Haste, Type1 = (byte)MagicSkillType.Buff, Moral = 1 });
        gameData.MagicType4Table.Returns(new Dictionary<int, MagicType4Data>
        {
            [Haste] = new() { Id = Haste, BuffType = (byte)BuffType.AttackSpeed, AttackSpeed = HasteAttackSpeed },
        });
    }, configureServices: services => services.AddSingleton<TimeProvider>(clock));

    private static (UserSession Attacker, List<Packet> Sent) Attacker(ServiceProvider provider)
    {
        var client = Substitute.For<IClient>();
        client.Id.Returns(Guid.NewGuid());
        var sent = new List<Packet>();
        client.SendPacket(Arg.Do<Packet>(sent.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sessions = provider.GetRequiredService<SessionManager>();
        var attacker = sessions.CreateSession(client, characterId: 910, accountId: 911);
        attacker.Name = "Swordsman";
        attacker.ZoneId = Zone;
        attacker.X = Here;
        attacker.Z = Here;
        attacker.Hp = 100;
        attacker.MaxHp = 100;
        attacker.AttackAmount = 100;
        attacker.PlayerAttackAmount = 100;
        attacker.Stats.TotalHit = 50;
        attacker.Stats.TotalHitrate = 10;
        attacker.Inventory[InventoryConstants.RightHand].ItemId = Sword;
        attacker.Inventory[InventoryConstants.RightHand].Count = 1;
        attacker.Inventory[InventoryConstants.RightHand].Durability = 100;
        sessions.Regions.AddToRegion(attacker);
        return (attacker, sent);
    }

    private static NpcInstance Monster(ServiceProvider provider, float x, ushort room = 0, short bulk = 0) =>
        provider.GetRequiredService<SessionManager>().Regions.SpawnNpc(new NpcInstance
        {
            IsMonster = true, NpcId = 750, Name = "Worm", ZoneId = Zone, Room = room, Bulk = bulk,
            X = x, Z = Here, SpawnX = x, SpawnZ = Here, Hp = 100_000, MaxHp = 100_000, Ac = 5, EvadeRate = 1,
            AttackRange = 3, SearchRange = 8, TracingRange = 20,
        });

    private static Task Swing(ServiceProvider provider, UserSession attacker, NpcInstance target) =>
        SwingAt(provider, attacker, target.UniqueId);

    private static async Task SwingAt(ServiceProvider provider, UserSession attacker, int targetId)
    {
        var packet = new Packet(GameOpcodes.GS_ATTACK);
        packet.WriteByte(1);
        packet.WriteByte(0);
        packet.WriteInt(targetId);
        packet.WriteShort(SwordDelay);
        packet.WriteShort(0);
        packet.WriteByte(0);
        packet.WriteByte(0);
        packet.ResetOffset();
        await provider.GetRequiredService<ICombatPacketCoordinator>().HandleAttackAsync(attacker.Client, packet);
    }

    private static Task Move(ServiceProvider provider, UserSession mover, short speed, byte echo, float? to = null)
    {
        var packet = new Packet(GameOpcodes.GS_MOVE);
        var x = (ushort)((to ?? mover.X) * 10);
        var z = (ushort)(mover.Z * 10);
        packet.WriteUShort(x);
        packet.WriteUShort(z);
        packet.WriteUShort(0);
        packet.WriteShort(speed);
        packet.WriteByte(echo);
        return provider.GetRequiredService<IWorldMovementService>().HandleMoveAsync(mover.Client, packet);
    }

    private static int Swings(List<Packet> sent) => sent.Count(packet => packet.GetOpcode() == (byte)GameOpcodes.GS_ATTACK);
}
