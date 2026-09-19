using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Network;
using LibreKO.Game.Protocol;
using LibreKO.Game.World;
using NSubstitute;

namespace LibreKO.Game.Tests;

public class MagicTimingTests
{
    private const int Slash = 101003;
    private const int Crash = 101005;
    private const int Archery = 102003;
    private const int Inferno = 110545;

    private sealed class StubClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    private static MagicData Magic(int id, int castTenths, int recastTenths, int useItem = 0) =>
        new()
        {
            Id = id,
            CastTime = (byte)castTenths,
            ReCastTime = (short)recastTenths,
            UseItem = useItem
        };

    private static UserSession Session() =>
        new(Substitute.For<IClient>(), characterId: 4242, accountId: 24) { Name = "Tester" };

    private static (MagicTimingService Timing, StubClock Clock) Subject()
    {
        var clock = new StubClock(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        return (new MagicTimingService(clock), clock);
    }

    [Fact]
    public void AVolleyReleasesOncePerArrowBeforeTheCooldownBites()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var volley = new MagicData { Id = 108515, Type1 = 2, CastTime = 13, ReCastTime = 30 };

        timing.OnCastAccepted(session, volley);
        clock.Advance(TimeSpan.FromSeconds(1.3));
        timing.CheckRelease(session, volley).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnVolleyAccepted(session, volley, arrows: 3);

        for (var arrow = 0; arrow < 3; arrow++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(120));
            timing.CheckRelease(session, volley).Should().Be(MagicTimingVerdict.Allowed, $"arrow {arrow + 1} of the volley");
            timing.OnReleaseAccepted(session, volley);
        }

        session.CastingSkillId.Should().Be(0, "the last arrow closes the cast");
        session.PendingArrowHits.Should().BeEmpty();
        timing.CheckRelease(session, volley).Should().Be(MagicTimingVerdict.OnCooldown, "a fourth hit is not part of the volley");
    }

    [Fact]
    public void ReCastTimeIsTenthsOfASecond()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var slash = Magic(Slash, castTenths: 0, recastTenths: 31);

        timing.OnCastAccepted(session, slash);
        timing.OnReleaseAccepted(session, slash);

        clock.Advance(TimeSpan.FromSeconds(2.5));
        timing.CheckCasting(session, slash).Should().Be(MagicTimingVerdict.OnCooldown);

        clock.Advance(TimeSpan.FromSeconds(0.8));
        timing.CheckCasting(session, slash).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void AZeroReCastSkillIsNeverBlockedByItsOwnCooldown()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var archery = Magic(Archery, castTenths: 13, recastTenths: 0);

        timing.OnCastAccepted(session, archery);
        timing.OnReleaseAccepted(session, archery);
        clock.Advance(TimeSpan.FromSeconds(1.45));

        timing.CheckCasting(session, archery).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void ASecondTimedCastIsRefusedWhileTheFirstIsStillInFlight()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var inferno = Magic(Inferno, castTenths: 15, recastTenths: 153);
        var otherNuke = Magic(110533, castTenths: 15, recastTenths: 10);

        timing.CheckCasting(session, inferno).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnCastAccepted(session, inferno);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        timing.CheckCasting(session, otherNuke).Should().Be(MagicTimingVerdict.AlreadyCasting);
    }

    [Fact]
    public void ReleasingBeforeTheCastTimeElapsedIsRefused()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var inferno = Magic(Inferno, castTenths: 15, recastTenths: 153);

        timing.OnCastAccepted(session, inferno);

        clock.Advance(TimeSpan.FromSeconds(0.5));
        timing.CheckRelease(session, inferno).Should().Be(MagicTimingVerdict.CastTooEarly);

        clock.Advance(TimeSpan.FromSeconds(1.0));
        timing.CheckRelease(session, inferno).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void ReleasingTheSkillThatArmedTheCooldownIsAllowed()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var slash = Magic(Slash, castTenths: 0, recastTenths: 31);

        timing.OnCastAccepted(session, slash);
        clock.Advance(TimeSpan.FromMilliseconds(40));

        timing.CheckRelease(session, slash).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void ASecondInstantSkillInsideTheBurstFloorIsRefused()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var slash = Magic(Slash, castTenths: 0, recastTenths: 31);
        var crash = Magic(Crash, castTenths: 0, recastTenths: 31);

        timing.OnCastAccepted(session, slash);

        timing.CheckCasting(session, crash).Should().Be(MagicTimingVerdict.TooSoonAfterLastSkill);

        clock.Advance(TimeSpan.FromMilliseconds(300));
        timing.CheckCasting(session, crash).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void HpAndMpPotionsShareOnePotionCooldown()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var hpPotion = Potion(490001, useItem: 389001000);
        var mpPotion = Potion(490004, useItem: 389004000);
        var scroll = Potion(460006, useItem: 310310010);
        scroll.CastTime = 15;
        scroll.ReCastTime = 3600;

        timing.CheckCasting(session, hpPotion).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnCastAccepted(session, hpPotion);
        clock.Advance(TimeSpan.FromMilliseconds(500));

        timing.CheckCasting(session, hpPotion).Should().Be(MagicTimingVerdict.OnCooldown);
        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.OnCooldown, "every potion shares the one cooldown");
        timing.CheckCasting(session, scroll).Should().Be(MagicTimingVerdict.Allowed, "a consumable with its own recast is not a potion");

        clock.Advance(TimeSpan.FromMilliseconds(1600));
        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.Allowed);
    }

    private static MagicData Potion(int id, int useItem) =>
        new() { Id = id, Type1 = 3, Moral = 1, ItemGroup = 9, UseItem = useItem, CastTime = 0, ReCastTime = 0 };

    [Fact]
    public void AConsumableIsExemptFromTheBurstFloor()
    {
        var (timing, _) = Subject();
        var session = Session();
        var slash = Magic(Slash, castTenths: 0, recastTenths: 31);
        var potion = Magic(500023, castTenths: 0, recastTenths: 0, useItem: 379090000);

        timing.OnCastAccepted(session, slash);

        timing.CheckCasting(session, potion).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void AConsumableReleasedAfterAnotherSkillWasAcceptedIsStillAllowed()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var potion = Magic(390001, castTenths: 5, recastTenths: 20, useItem: 389001000);
        var slash = Magic(Slash, castTenths: 0, recastTenths: 31);

        timing.CheckCasting(session, potion).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnCastAccepted(session, potion);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        timing.CheckCasting(session, slash).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnCastAccepted(session, slash);
        clock.Advance(TimeSpan.FromMilliseconds(400));

        timing.CheckRelease(session, potion).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnReleaseAccepted(session, potion);
        timing.CheckRelease(session, slash).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void ASkillReleasedAfterAPotionWasAcceptedIsStillAllowed()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var slash = Magic(Slash, castTenths: 0, recastTenths: 31);
        var potion = Magic(390001, castTenths: 0, recastTenths: 20, useItem: 389001000);

        timing.OnCastAccepted(session, slash);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        timing.OnCastAccepted(session, potion);
        clock.Advance(TimeSpan.FromMilliseconds(300));

        timing.CheckRelease(session, slash).Should().Be(MagicTimingVerdict.Allowed);
        timing.CheckRelease(session, potion).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void ARangedDrawMayBeReleasedFromItsCommitPoint()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var arcShot = Magic(102010, castTenths: 13, recastTenths: 30);
        arcShot.Type1 = 2;

        timing.OnCastAccepted(session, arcShot);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        timing.CheckRelease(session, arcShot).Should().Be(MagicTimingVerdict.CastTooEarly);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        timing.CheckRelease(session, arcShot).Should().Be(MagicTimingVerdict.Allowed,
            "the arrow leaves the bow 400 ms into the draw; moving after that only cuts the animation");
    }

    [Fact]
    public void ANonRangedCastStillWaitsForItsWholeCastTime()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var inferno = Magic(Inferno, castTenths: 13, recastTenths: 30);
        inferno.Type1 = 3;

        timing.OnCastAccepted(session, inferno);
        clock.Advance(TimeSpan.FromMilliseconds(600));
        timing.CheckRelease(session, inferno).Should().Be(MagicTimingVerdict.CastTooEarly);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        timing.CheckRelease(session, inferno).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void ATimedConsumableDoesNotOccupyTheCastSlot()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var sweepingPotion = Magic(490201, castTenths: 10, recastTenths: 600, useItem: 379098000);
        var inferno = Magic(Inferno, castTenths: 15, recastTenths: 153);

        timing.OnCastAccepted(session, sweepingPotion);
        clock.Advance(TimeSpan.FromMilliseconds(50));

        timing.CheckCasting(session, inferno).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void CancellingRefundsTheCooldownAndFreesTheCastSlot()
    {
        var (timing, _) = Subject();
        var session = Session();
        var inferno = Magic(Inferno, castTenths: 15, recastTenths: 153);

        timing.OnCastAccepted(session, inferno);
        timing.OnCastAborted(session, inferno.Id);

        session.CastingSkillId.Should().Be(0);
        session.SkillCooldowns.Should().NotContainKey(inferno.Id);
    }

    [Fact]
    public void AnAbandonedCastStopsBlockingOnceItsGraceExpires()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var inferno = Magic(Inferno, castTenths: 15, recastTenths: 153);
        var otherNuke = Magic(110533, castTenths: 15, recastTenths: 10);

        timing.OnCastAccepted(session, inferno);
        clock.Advance(TimeSpan.FromSeconds(6));

        timing.CheckCasting(session, otherNuke).Should().Be(MagicTimingVerdict.Allowed);
    }
}
