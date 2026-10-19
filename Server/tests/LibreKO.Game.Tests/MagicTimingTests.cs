using FluentAssertions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Services;
using LibreKO.Common.Enums;
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
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
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
        var gameData = Substitute.For<IGameDataService>();
        gameData.MagicType3Table.Returns(RestoreEffects);
        return (new MagicTimingService(gameData, clock), clock);
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
        var hpPotion = Restore(HolyWater, useItem: 389010000, castTenths: 5, recastTenths: 20);
        var mpPotion = Restore(PotionOfSpirit, useItem: 389016000, castTenths: 5, recastTenths: 20);

        timing.CheckCasting(session, hpPotion).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnCastAccepted(session, hpPotion);
        clock.Advance(TimeSpan.FromMilliseconds(50));

        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.OnCooldown, "every potion shares the one cooldown");

        clock.Advance(TimeSpan.FromMilliseconds(450));
        timing.CheckRelease(session, hpPotion).Should().Be(MagicTimingVerdict.Allowed, "the accepted potion still lands");
        timing.OnReleaseAccepted(session, hpPotion);
        timing.CheckCasting(session, hpPotion).Should().Be(MagicTimingVerdict.OnCooldown);
        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.OnCooldown);

        clock.Advance(TimeSpan.FromMilliseconds(1500));
        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void APotionWithoutACastTimeHoldsTheOtherPotionsFromItsRelease()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var riceCake = Restore(RiceCake, useItem: 389001000, castTenths: 0, recastTenths: 0);
        var greenRiceCake = Restore(GreenRiceCake, useItem: 389004000, castTenths: 0, recastTenths: 0);
        var holyWater = Restore(HolyWater, useItem: 389010000, castTenths: 5, recastTenths: 20);

        timing.CheckRelease(session, riceCake).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnReleaseAccepted(session, riceCake);
        clock.Advance(TimeSpan.FromMilliseconds(50));

        timing.CheckRelease(session, greenRiceCake).Should().Be(MagicTimingVerdict.OnCooldown);
        timing.CheckCasting(session, holyWater).Should().Be(MagicTimingVerdict.OnCooldown);

        clock.Advance(TimeSpan.FromSeconds(2));
        timing.CheckRelease(session, greenRiceCake).Should().Be(MagicTimingVerdict.Allowed);
    }

    [Fact]
    public void APotionWithoutACastTimeIsRefusedWhileACastPotionHoldsTheCooldown()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var holyWater = Restore(HolyWater, useItem: 389010000, castTenths: 5, recastTenths: 20);
        var greenRiceCake = Restore(GreenRiceCake, useItem: 389004000, castTenths: 0, recastTenths: 0);

        timing.OnCastAccepted(session, holyWater);
        clock.Advance(TimeSpan.FromMilliseconds(100));

        timing.CheckRelease(session, greenRiceCake).Should().Be(MagicTimingVerdict.OnCooldown);
    }

    [Fact]
    public void ThePotionCooldownAllowsForLatency()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var hpPotion = Restore(HolyWater, useItem: 389010000, castTenths: 5, recastTenths: 20);
        var mpPotion = Restore(PotionOfSpirit, useItem: 389016000, castTenths: 5, recastTenths: 20);

        timing.OnCastAccepted(session, hpPotion);

        clock.Advance(TimeSpan.FromMilliseconds(1700));
        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.OnCooldown);

        clock.Advance(TimeSpan.FromMilliseconds(100));
        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.Allowed,
            "the client waits the full 2 s from its own send; the packets may arrive closer together");
    }

    [Fact]
    public void APotionWithALongerRecastKeepsItBesideTheSharedCooldown()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var waterOfLife = Restore(WaterOfLife, useItem: 389060000, castTenths: 5, recastTenths: 25);
        var mpPotion = Restore(PotionOfSpirit, useItem: 389016000, castTenths: 5, recastTenths: 20);

        timing.OnCastAccepted(session, waterOfLife);
        clock.Advance(TimeSpan.FromSeconds(2));

        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.Allowed);
        timing.CheckCasting(session, waterOfLife).Should().Be(MagicTimingVerdict.OnCooldown);
    }

    [Fact]
    public void AMaestroPotionSharesThePotionCooldown()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var recoverHealth = Restore(RecoverHealth, useItem: 0, castTenths: 5, recastTenths: 20);
        var mpPotion = Restore(PotionOfSpirit, useItem: 389016000, castTenths: 5, recastTenths: 20);

        timing.CheckCasting(session, recoverHealth).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnCastAccepted(session, recoverHealth);
        clock.Advance(TimeSpan.FromMilliseconds(50));

        timing.CheckCasting(session, mpPotion).Should().Be(MagicTimingVerdict.OnCooldown);
    }

    [Fact]
    public void ConsumablesAndSkillsThatAreNotPotionsIgnoreThePotionCooldown()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var holyWater = Restore(HolyWater, useItem: 389010000, castTenths: 5, recastTenths: 20);
        var hyperHealing = Restore(HyperHealing, useItem: 310310010, castTenths: 15, recastTenths: 3600);
        var magicHammer = Restore(MagicHammer, useItem: 399288000, castTenths: 5, recastTenths: 25);
        var returnToLife = Restore(ReturnToLife, useItem: 0, castTenths: 0, recastTenths: 51);

        timing.OnCastAccepted(session, holyWater);
        clock.Advance(TimeSpan.FromMilliseconds(50));

        timing.CheckCasting(session, hyperHealing).Should().Be(MagicTimingVerdict.Allowed, "a heal over time is not a potion");
        timing.CheckCasting(session, magicHammer).Should().Be(MagicTimingVerdict.Allowed, "a repair is not a potion");
        timing.CheckRelease(session, returnToLife).Should().Be(MagicTimingVerdict.Allowed, "a class skill is not a potion");
    }

    private const int RiceCake = 490001;
    private const int GreenRiceCake = 490004;
    private const int HolyWater = 490010;
    private const int PotionOfSpirit = 490016;
    private const int WaterOfLife = 490060;
    private const int RecoverHealth = 490076;
    private const int HyperHealing = 460006;
    private const int MagicHammer = 490153;
    private const int ReturnToLife = 105740;

    private static readonly Dictionary<int, MagicType3Data> RestoreEffects = new()
    {
        [RiceCake] = Effect(RiceCake, MagicDirectType.Health, firstDamage: 200),
        [GreenRiceCake] = Effect(GreenRiceCake, MagicDirectType.Mana, firstDamage: 200),
        [HolyWater] = Effect(HolyWater, MagicDirectType.Health, firstDamage: 45),
        [PotionOfSpirit] = Effect(PotionOfSpirit, MagicDirectType.Mana, firstDamage: 120),
        [WaterOfLife] = Effect(WaterOfLife, MagicDirectType.Health, firstDamage: 90),
        [RecoverHealth] = Effect(RecoverHealth, MagicDirectType.HealthPurchase, firstDamage: 720),
        [HyperHealing] = Effect(HyperHealing, MagicDirectType.Health, firstDamage: 0, timeDamage: 50, duration: 60),
        [MagicHammer] = Effect(MagicHammer, MagicDirectType.Durability, firstDamage: 2000),
        [ReturnToLife] = Effect(ReturnToLife, MagicDirectType.Health, firstDamage: 250),
    };

    private static MagicType3Data Effect(int id, MagicDirectType directType, short firstDamage, short timeDamage = 0, byte duration = 0) =>
        new() { Id = id, DirectType = (byte)directType, FirstDamage = firstDamage, TimeDamage = timeDamage, Duration = duration };

    private static MagicData Restore(int id, int useItem, int castTenths, int recastTenths) =>
        new() { Id = id, Type1 = 3, Moral = 1, ItemGroup = 9, UseItem = useItem, CastTime = (byte)castTenths, ReCastTime = (short)recastTenths };

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
    public void ALaunchedSpellFreesTheCastSlotAndStillLandsOnItsOwnCooldown()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var fireBall = Magic(110515, castTenths: 15, recastTenths: 30);
        fireBall.Type1 = 3;
        var fireBlast = Magic(110535, castTenths: 15, recastTenths: 50);
        fireBlast.Type1 = 3;

        timing.OnCastAccepted(session, fireBall);
        var cooldownFromTheCast = session.SkillCooldowns[fireBall.Id];
        clock.Advance(TimeSpan.FromSeconds(1.5));
        timing.CheckRelease(session, fireBall).Should().Be(MagicTimingVerdict.Allowed);
        timing.OnFlightLaunched(session, fireBall);

        clock.Advance(TimeSpan.FromMilliseconds(300));
        timing.CheckCasting(session, fireBlast).Should().Be(MagicTimingVerdict.Allowed,
            "the fireball has left the caster, the next spell may start while it flies");
        timing.OnCastAccepted(session, fireBlast);

        clock.Advance(TimeSpan.FromMilliseconds(500));
        timing.CheckRelease(session, fireBall).Should().Be(MagicTimingVerdict.Allowed, "the fireball lands");
        timing.OnReleaseAccepted(session, fireBall);
        session.CastingSkillId.Should().Be(fireBlast.Id, "the fireball's landing must not end the next cast");
        session.SkillCooldowns[fireBall.Id].Should().Be(cooldownFromTheCast,
            "the cooldown runs from the cast, the landing must not re-arm it");
    }

    [Fact]
    public void ALaunchedArrowKeepsTheDrawTiming()
    {
        var (timing, clock) = Subject();
        var session = Session();
        var arcShot = Magic(102010, castTenths: 13, recastTenths: 30);
        arcShot.Type1 = 2;

        timing.OnCastAccepted(session, arcShot);
        clock.Advance(TimeSpan.FromSeconds(1.3));
        timing.OnFlightLaunched(session, arcShot);

        session.CastingSkillId.Should().Be(arcShot.Id);
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
